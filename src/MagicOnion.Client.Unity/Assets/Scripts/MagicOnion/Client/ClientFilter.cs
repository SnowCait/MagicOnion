﻿using Grpc.Core;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MagicOnion.Client
{
    public interface IClientFilter
    {
        ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next);
    }

    public abstract class RequestContext
    {
        public string MethodPath { get; }
        public CallOptions CallOptions { get; }
        public Type ResponseType { get; }
        public abstract Type RequestType { get; }

        Dictionary<string, object> items;
        public IDictionary<string, object> Items
        {
            get
            {
                if (items == null)
                {
                    items = new Dictionary<string, object>();
                }
                return items;
            }
        }

        // internal use to avoid lambda capture.
        internal MagicOnionClientBase Client { get; }
        internal IClientFilter[] Filters { get; }
        internal Func<RequestContext, ResponseContext> RequestMethod { get; }

        internal RequestContext(MagicOnionClientBase client, string methodPath, CallOptions callOptions, Type responseType, IClientFilter[] filters, Func<RequestContext, ResponseContext> requestMethod)
        {
            this.Client = client;
            this.MethodPath = methodPath;
            this.CallOptions = callOptions;
            this.ResponseType = responseType;
            this.Filters = filters;
            this.RequestMethod = requestMethod;
        }
    }

    public class RequestContext<T> : RequestContext
    {
        public T Request { get; }
        public override Type RequestType => typeof(T);

        public RequestContext(T request, MagicOnionClientBase client, string methodPath, CallOptions callOptions, Type responseType, IClientFilter[] filters, Func<RequestContext, ResponseContext> requestMethod)
            : base(client, methodPath, callOptions, responseType, filters, requestMethod)
        {
            this.Request = request;
        }
    }

    public abstract class ResponseContext : IDisposable
    {
        public abstract Task<Metadata> ResponseHeadersAsync { get; }
        public abstract Status GetStatus();
        public abstract Metadata GetTrailers();
        public abstract void Dispose();
        public abstract Type ResponseType { get; }

        public abstract Task<ResponseContext> WaitResponseAsync();

        public ResponseContext<T> As<T>()
        {
            return this as ResponseContext<T>;
        }

        public Task<T> GetResponseAs<T>()
        {
            var t = this as ResponseContext<T>;
            if (t == null) return Task.FromResult(default(T));

            return t.ResponseAsync;
        }
    }

    public sealed class ResponseContext<T> : ResponseContext
    {
        readonly AsyncUnaryCall<byte[]> inner;
        readonly IFormatterResolver resolver;
        readonly bool isMock;
        bool deserialized;

        T responseObject; // cache value.

        // mock
        readonly Metadata trailers;
        readonly Metadata responseHeaders;
        readonly Status status;

        public ResponseContext(AsyncUnaryCall<byte[]> inner, IFormatterResolver resolver)
        {
            this.isMock = false;
            this.inner = inner;
            this.resolver = resolver;
        }

        public ResponseContext(T responseObject)
            : this(responseObject, new Metadata(), new Metadata(), Status.DefaultSuccess)
        {
        }

        public ResponseContext(T responseObject, Metadata trailers, Metadata responseHeaders, Status status)
        {
            this.isMock = true;
            this.responseObject = responseObject;
            this.trailers = trailers;
            this.responseHeaders = responseHeaders;
            this.status = status;
        }

        async Task<T> Deserialize()
        {
            if (deserialized)
            {
                return responseObject;
            }
            else
            {
                var bytes = await inner.ResponseAsync.ConfigureAwait(false);
                responseObject = LZ4MessagePackSerializer.Deserialize<T>(bytes, resolver);
                deserialized = true;
                return responseObject;
            }
        }

        public Task<T> ResponseAsync => !isMock ? Deserialize() : Task.FromResult(responseObject);

        public override async Task<ResponseContext> WaitResponseAsync()
        {
            await ResponseAsync;
            return this;
        }

        public override Type ResponseType => typeof(T);

        public override Task<Metadata> ResponseHeadersAsync => !isMock ? inner.ResponseHeadersAsync : Task.FromResult(responseHeaders);

        public override void Dispose()
        {
            if (!isMock)
            {
                inner.Dispose();
            }
        }

        public override Status GetStatus()
        {
            return !isMock ? inner.GetStatus() : status;
        }

        public override Metadata GetTrailers()
        {
            return !isMock ? inner.GetTrailers() : trailers;
        }

        public ResponseContext<T> WithNewResult(T result)
        {
            if (isMock)
            {
                return new ResponseContext<T>(result, trailers, responseHeaders, status);
            }
            else
            {
                var newContext = new ResponseContext<T>(inner, resolver);
                newContext.deserialized = true;
                newContext.responseObject = result;
                return newContext;
            }
        }
    }
}
