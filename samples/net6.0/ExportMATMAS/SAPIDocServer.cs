using System.Text.Encodings.Web;
using System.Text.Json;
using Dbosoft.YaNco;
using LanguageExt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
// ReSharper disable StringLiteralTypo

namespace ExportMATMAS;

// ReSharper disable once InconsistentNaming
public class SAPIDocServer : BackgroundService
{
    private readonly IConfiguration _configuration;
 

    public SAPIDocServer(IConfiguration configuration)
    {
        _configuration = configuration; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {


        var serverSettings = new Dictionary<string, string>
        {
            
            { "SYSID", _configuration["saprfc:sysid"] },
            { "PROGRAM_ID", _configuration["saprfc:program_id"] },
            { "GWHOST", _configuration["saprfc:ashost"] },
            { "GWSERV", "sapgw" + _configuration["saprfc:sysnr"] },
            { "REG_COUNT", "1" },
            { "TRACE", "0" }
        };

        var clientSettings = new Dictionary<string, string>
        {
            { "ashost", _configuration["saprfc:ashost"] },
            { "sysnr", _configuration["saprfc:sysnr"] },
            { "client", _configuration["saprfc:client"] },
            { "user", _configuration["saprfc:user"] },
            { "passwd", _configuration["saprfc:passwd"] },
            { "lang", "EN" }
        };
       

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        using var rfcServer = await new ServerBuilder(serverSettings)
            .WithTransactionalRfc(new EverythingIsOkTransactionalRfcHandler())
            .WithClientConnection(clientSettings,
                c => c
                    .WithFunctionHandler("IDOC_INBOUND_ASYNCHRONOUS", ProcessInboundIDoc))
            .Build()
            .StartOrException();


        Console.WriteLine("MATMAS IDOC Server is ready");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
        }
        catch (TaskCanceledException)
        {
        }


        await rfcServer.Stop().ToEither();

        Console.WriteLine("Server stopped");
    }


    private static EitherAsync<RfcErrorInfo, Unit> ProcessInboundIDoc(CalledFunction cf)
    {
        return cf
            .Input(i =>

                // extract IDoc data from incoming RFC call
                from data in i.MapTable("IDOC_DATA_REC_40",
                    s => 
                        from iDocNo in s.GetField<string>("DOCNUM")
                        from segment in s.GetField<string>("SEGNAM")
                        from segmentNo in s.GetField<int>("SEGNUM")
                        from parentNo in s.GetField<int>("PSGNUM")
                        from level in s.GetField<int>("HLEVEL")
                        from data in s.GetField<string>("SDATA")
                        select new IDocDataRecord(iDocNo, segment, segmentNo, parentNo, level, data)
                        )
                select data.ToSeq())
            .ProcessAsync(data =>
            {
                cf.RfcRuntime.GetServerCallContext(cf.RfcHandle).Map(context =>
                {
                    Console.WriteLine("rfc call type: " + context.CallType);
                    Console.WriteLine("called transactionId: " + context.TransactionId);
                    return context;
                });

                // open a client connection to read type definitions if not only cached
                return cf.UseRfcContextAsync(context =>
                {
                    return (from connection in context.GetConnection()
                        from materialMaster in ExtractMaterialMaster(connection, data)
                        select materialMaster).Match(
                        r => Console.WriteLine("Received Material:\n" + PrettyPrintMaterial(r)),
                        l => Console.WriteLine("Error: " + l.Message));
                });


            })
            .NoReply();
    }


    private static EitherAsync<RfcErrorInfo, MaterialMasterRecord> ExtractMaterialMaster(IConnection connection, 
        Seq<IDocDataRecord> data)
    {
        return

            //extract some client data of material master
            from clientSegment in FindRequiredSegment("E1MARAM", data)
            from material in MapSegment(connection, clientSegment, s =>
                from materialNo in s.GetField<string>("MATNR")
                from unit in s.GetField<string>("MEINS")  
                select new
                {
                    MaterialNo = materialNo, 
                    ClientData = new ClientData(unit)
                })

            //extract descriptions data of material master
            from descriptionData in MapSegments(connection, FindSegments("E1MAKTM", data), s =>
                from language in s.GetField<string>("SPRAS_ISO")
                from description in s.GetField<string>("MAKTX")
                select new DescriptionData(language, description))

            //extract some plant data of material master
            from plantData in MapSegments(connection, FindSegments("E1MARCM", data), s =>
                from plant in s.GetField<string>("WERKS")
                from purchasingGroup in s.GetField<string>("EKGRP")
                select new PlantData(plant, purchasingGroup)
            )
            select new MaterialMasterRecord(
                material.MaterialNo, 
                material.ClientData, 
                descriptionData.ToArray(), 
                plantData.ToArray());
    }

    private static EitherAsync<RfcErrorInfo, T> MapSegment<T>(IConnection connection, 
        IDocDataRecord data, Func<IStructure,Either<RfcErrorInfo, T>> mapFunc)
    {
        //to convert the segment we create a temporary structure of the segment definition type
        //and "move" the segment data into it. 
        return connection.CreateStructure(_segment2Type[data.Segment]).Use(structure =>
        {
            return from _ in structure.Bind(s => s.SetFromString(data.Data).ToAsync())
                from res in structure.Bind(s => mapFunc(s).ToAsync())
                select res;
        });

    }

    private static EitherAsync<RfcErrorInfo, Seq<T>> MapSegments<T>(IConnection connection,
        Seq<IDocDataRecord> data, Func<IStructure, Either<RfcErrorInfo, T>> mapFunc)
    {
        return data.Map(segment => MapSegment(connection, segment, mapFunc))
            .Traverse(l => l);
    }

    private static EitherAsync<RfcErrorInfo, IDocDataRecord> FindRequiredSegment(
        string typeName, Seq<IDocDataRecord> records )
    {
        var segmentName = _type2Segment[typeName];
        return records.Find(x => x.Segment == segmentName)
            .ToEither(RfcErrorInfo.Error($"Segment {segmentName} not found"))
            .ToAsync();
    }

    private static Seq<IDocDataRecord> FindSegments(
        string typeName, Seq<IDocDataRecord> records)
    {
        var segmentName = _type2Segment[typeName];
        return records.Filter(x => x.Segment == segmentName);
    }

    // for a known IDoc type you used fixed segment to type mapping
    // a more generic way would be looking up segment names from RFM IDOCTYPE_READ_COMPLETE

    private static HashMap<string, string> _segment2Type = new(new[]
    {
        ("E2MARAM009", "E1MARAM"), // client data, MATMAS05
        ("E2MARCM008", "E1MARCM"), // plant data, MATMAS05
        ("E2MAKTM001", "E1MAKTM"), // descriptions, MATMAS05
    });

    private static HashMap<string, string> _type2Segment = new(new[]
    {
        ("E1MARAM", "E2MARAM009" ),
        ("E1MARCM", "E2MARCM008"),
        ("E1MAKTM", "E2MAKTM001")
    });

    private static string PrettyPrintMaterial(MaterialMasterRecord record)
    {
        return JsonSerializer.Serialize(record, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        //json don't has to be valid, so disable text encoding for unicode chars
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
}


// ReSharper disable InconsistentNaming
// ReSharper restore InconsistentNaming