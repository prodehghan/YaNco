﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LanguageExt;

namespace Dbosoft.YaNco
{
    public static class FunctionalFunctionsExtensions
    {
        /// <summary>
        /// Commits current transaction in ABAP backend.
        /// This method accepts a <see cref="EitherAsync{RfcErrorInfo,R1}"/> with any right value and returns it after commit.
        /// </summary>
        /// <typeparam name="R1">Type of function result</typeparam>
        /// <param name="self"></param>
        /// <param name="context"></param>
        /// <returns>A <see cref="EitherAsync{RfcErrorInfo,R1}"/> with the Right value of argument self or the left value.</returns>
        public static EitherAsync<RfcErrorInfo, R1> Commit<R1>(this EitherAsync<RfcErrorInfo, R1> self,
            IRfcContext context)
        {
            return self.Bind(res => context.Commit().Map(u => res));
        }

        /// <summary>
        /// Commits current transaction in ABAP backend and waits for the commit to be completed.
        /// This method accepts a <see cref="EitherAsync{RfcErrorInfo,R1}"/> with any right value and returns it after commit.
        /// </summary>
        /// <typeparam name="R1">Type of function result</typeparam>
        /// <param name="self"></param>
        /// <param name="context"></param>
        /// <returns>A <see cref="EitherAsync{RfcErrorInfo,R1}"/> with the Right value of argument self or the left value.</returns>
        public static EitherAsync<RfcErrorInfo, R1> CommitAndWait<R1>(this EitherAsync<RfcErrorInfo, R1> self,
            IRfcContext context)
        {
            return self.Bind(res => context.CommitAndWait().Map(u => res));
        }

        /// <summary>
        /// This methods extracts the value of a RETURN structure (BAPIRET or BAPIRET2) and processes it's value as
        /// left value if return contains a non-successful result (abort or error). 
        /// This method accepts a <see cref="EitherAsync{RfcErrorInfo,IFunction}"/> with <see cref="IFunction"/> as right value and returns it if return contains a successful result.
        /// </summary>
        /// <returns>A <see cref="EitherAsync{RfcErrorInfo,IFunction}"/> with the function as right value or the left value.</returns>
        public static EitherAsync<RfcErrorInfo, IFunction> HandleReturn(this EitherAsync<RfcErrorInfo, IFunction> self)
        {
            return self.ToEither().Map(f => f.HandleReturn()).ToAsync();
        }

        public static EitherAsync<RfcErrorInfo, Unit> AsUnit(this EitherAsync<RfcErrorInfo, IFunction> self)
        {
            return self.Map(_ => Unit.Default);
        }

        /// <summary>
        /// This methods extracts the value of a RETURN structure (BAPIRET or BAPIRET2) and processes it's value as
        /// left value if return contains a non-successful result (abort or error). 
        /// This method accepts a <see cref="Either{RfcErrorInfo,IFunction}"/> with <see cref="IFunction"/> as right value and returns it if return contains a successful result.
        /// </summary>
        /// <param name="self"></param>
        /// <returns>A <see cref="Either{RfcErrorInfo,IFunction}"/> with the function as right value or the left value.</returns>
        public static Either<RfcErrorInfo, IFunction> HandleReturn(this Either<RfcErrorInfo, IFunction> self)
        {
            return self.Bind(f => (
                from ret in f.GetStructure("RETURN")
                from type in ret.GetField<string>("TYPE")
                from id in ret.GetField<string>("ID")
                from number in ret.GetField<string>("NUMBER")
                from message in ret.GetField<string>("MESSAGE")
                from v1 in ret.GetField<string>("MESSAGE_V1")
                from v2 in ret.GetField<string>("MESSAGE_V2")
                from v3 in ret.GetField<string>("MESSAGE_V3")
                from v4 in ret.GetField<string>("MESSAGE_V4")

                from _ in ErrorOrResult(f, type, id, number, message, v1, v2, v3, v4)
                select f));

        }


        /// <summary>
        /// This methods extracts the value of a RETURN table (BAPIRET or BAPIRET2) and processes it's value as
        /// left value if return contains a non-successful result (abort or error). 
        /// This method accepts a <see cref="EitherAsync{RfcErrorInfo,IFunction}"/> with <see cref="IFunction"/> as right value and returns it if return contains a successful result.
        /// </summary>
        /// <returns>A <see cref="EitherAsync{RfcErrorInfo,IFunction}"/> with the function as right value or the left value.</returns>
        public static EitherAsync<RfcErrorInfo, IFunction> HandleReturnTable(this EitherAsync<RfcErrorInfo, IFunction> self)
        {
            return self.ToEither().Map(f => f.HandleReturnTable()).ToAsync();
        }

        /// <summary>
        /// This methods extracts the value of a RETURN table (BAPIRET or BAPIRET2) and processes it's value as
        /// left value if return contains a non-successful result (abort or error). 
        /// This method accepts a <see cref="Either{RfcErrorInfo,IFunction}"/> with <see cref="IFunction"/> as right value and returns it if return contains a successful result.
        /// </summary>
        /// <param name="self"></param>
        /// <returns>A <see cref="Either{RfcErrorInfo,IFunction}"/> with the function as right value or the left value.</returns>
        public static Either<RfcErrorInfo, IFunction> HandleReturnTable(this Either<RfcErrorInfo, IFunction> self)
        {
            return self.Bind(f => (
                from retTab in f.GetTable("RETURN")
                from messages in retTab.Rows.Map(r =>
                    from type in r.GetField<string>("TYPE")
                    from id in r.GetField<string>("ID")
                    from number in r.GetField<string>("NUMBER")
                    from message in r.GetField<string>("MESSAGE")
                    from v1 in r.GetField<string>("MESSAGE_V1")
                    from v2 in r.GetField<string>("MESSAGE_V2")
                    from v3 in r.GetField<string>("MESSAGE_V3")
                    from v4 in r.GetField<string>("MESSAGE_V4")
                    select new ReturnData
                    {
                        Type = type,
                        Id = id,
                        Number = number,
                        Message = message,
                        MessageV1 = v1,
                        MessageV2 = v2,
                        MessageV3 = v3,
                        MessageV4 = v4
                    }
                ).Traverse(l => l)
                from _ in ErrorOrResult(f, messages)
                select f));

        }

        private class ReturnData
        {
            public string Type { get; set; }
            public string Id { get; set; }
            public string Number { get; set; }
            public string Message { get; set; }

            public string MessageV1 { get; set; }
            public string MessageV2 { get; set; }
            public string MessageV3 { get; set; }
            public string MessageV4 { get; set; }

        }

        private static Either<RfcErrorInfo, TResult> ErrorOrResult<TResult>(TResult result, IEnumerable<ReturnData> messages)
        {
            var messagesArray = messages as ReturnData[] ?? messages.ToArray();

            if (!messagesArray.Any(x => x.Type.Contains('E') || x.Type.Contains('A')))
                return result;

            var failedMessage = messagesArray.FirstOrDefault(x => x.Type.Contains('A'))
                                ?? messagesArray.FirstOrDefault(x => x.Type.Contains('E'));

            return new RfcErrorInfo(RfcRc.RFC_ABAP_MESSAGE, RfcErrorGroup.ABAP_APPLICATION_FAILURE, "",
                failedMessage?.Message, failedMessage?.Id,
                failedMessage?.Type, failedMessage?.Number,
                failedMessage?.MessageV1, failedMessage?.MessageV2,
                failedMessage?.MessageV3, failedMessage?.MessageV4);
        }

        private static Either<RfcErrorInfo, TResult> ErrorOrResult<TResult>(TResult result, string type, string id, string number, string message, string v1, string v2, string v3, string v4)
        {
            if (type.Contains('E') || type.Contains('A'))
                return new RfcErrorInfo(RfcRc.RFC_ABAP_MESSAGE, RfcErrorGroup.ABAP_APPLICATION_FAILURE, "", message, id, type, number, v1, v2, v3, v4);

            return result;
        }

        // ReSharper disable InconsistentNaming

        /// <summary>
        /// CallFunction with input and output and <see cref="RfcErrorInfo"/> lifted input and output functions.
        /// </summary>
        /// <remarks>
        /// The input parameter of this method is a function that maps from a <see cref="Either{RfcErrorInfo,IFunction}"/>
        /// to any kind of type. The input type <typeparam name="TRInput"></typeparam> itself is not used any more
        /// after calling the input mapping.
        /// The output parameter of this method is also a function that maps from a <see cref="Either{RfcErrorInfo,IFunction}"/>
        /// to any kind of type. The output type <typeparam name="TResult"></typeparam> is returned after processing the ABAP function.
        ///
        /// You should use the methods defined on <seealso cref="FunctionalDataContainerExtensions"/> within the mapping functions to map from .NET
        /// types to SAP function fields and back from SAP function fields to .NET. 
        /// </remarks>
        /// <typeparam name="TRInput"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="context">current RFC context</param>
        /// <param name="functionName">name of the function as defined in SAP backend</param>
        /// <param name="Input">Input function lifted in either monad.</param>
        /// <param name="Output">Output function lifted in either monad.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Result of output mapping function.</returns>
        public static EitherAsync<RfcErrorInfo, TResult> CallFunction<TRInput, TResult>(
            this IRfcContext context, 
            string functionName, 
            Func<Either<RfcErrorInfo, IFunction>, Either<RfcErrorInfo, TRInput>> Input, 
            Func<Either<RfcErrorInfo, IFunction>, Either<RfcErrorInfo, TResult>> Output,
            CancellationToken cancellationToken = default)
        {
            return context.CreateFunction(functionName).Use(
                ef => ef.Bind(func => 
                    
                    from input in Input(Prelude.Right(func)).ToAsync()
                    from _ in context.InvokeFunction(func, cancellationToken)
                    from output in Output(Prelude.Right(func)).ToAsync()
                    select output)
                );

        }

        /// <summary>
        /// CallFunction with RfcErrorInfo lifted output.
        /// </summary>
        /// <remarks>
        /// The output parameter of this method is a function that maps from a <see cref="Either{RfcErrorInfo,IFunction}"/>
        /// to any kind of type. The output type <typeparam name="TResult"></typeparam> is returned after processing the ABAP function.
        ///
        /// You should use the methods defined on <seealso cref="FunctionalDataContainerExtensions"/> within the output mapping function to map
        /// from SAP function fields to .NET. 
        /// </remarks>

        /// <typeparam name="TResult"></typeparam>
        /// <param name="context">RFC context</param>
        /// <param name="functionName">ABAP function name</param>
        /// <param name="Output">Output function lifted in either monad.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Result of output mapping function.</returns>
        public static EitherAsync<RfcErrorInfo, TResult> CallFunction<TResult>(
            this IRfcContext context, 
            string functionName, 
            Func<Either<RfcErrorInfo,IFunction>, Either<RfcErrorInfo, TResult>> Output,
            CancellationToken cancellationToken = default)
        {
            return context.CreateFunction(functionName).Use(
                ef => ef.Bind(func =>
                    from _ in context.InvokeFunction(func, cancellationToken)
                    from output in Output(Prelude.Right(func)).ToAsync()
                    select output)
            );
        }

        /// <summary>
        /// CallFunction without input or output.
        /// </summary>
        /// <param name="context">RFC context</param>
        /// <param name="functionName">ABAP function name</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Unit</returns>
        public static EitherAsync<RfcErrorInfo, Unit> CallFunctionOneWay(
            this IRfcContext context, 
            string functionName,
            CancellationToken cancellationToken = default)
        {
            return context.CreateFunction(functionName).Use(
                func => func.Bind(f => context.InvokeFunction(f,cancellationToken)));

        }

        /// <summary>
        /// CallFunction with RfcInfo lifted input and no output.
        /// </summary>
        /// <remarks>
        /// The input parameter of this method is a function that maps from a <see cref="Either{RfcErrorInfo,IFunction}"/>
        /// to any kind of type. The input type <typeparam name="TRInput"></typeparam> itself is not used any more
        /// after calling the input mapping.
        /// You should use the methods defined on <seealso cref="FunctionalDataContainerExtensions"/> within the input mapping functions to map from .NET
        /// types to SAP function fields. 
        /// </remarks>
        /// <param name="context">RFC context</param>
        /// <param name="functionName">ABAP function name</param>
        /// <param name="Input">Input function lifted in either monad.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Unit</returns>
        public static EitherAsync<RfcErrorInfo, Unit> CallFunctionOneWay<TRInput>(
            this IRfcContext context, 
            string functionName, 
            Func<Either<RfcErrorInfo, IFunction>, Either<RfcErrorInfo, TRInput>> Input,
            CancellationToken cancellationToken = default)
        {
            return context.CreateFunction(functionName).Use(
                ef => ef.Bind(func =>

                    from input in Input(Prelude.Right(func)).ToAsync()
                    from _ in context.InvokeFunction(func, cancellationToken)
                    select Unit.Default)
            );
        }

    }
}
