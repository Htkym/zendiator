using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

// Handwritten typed contract. No emitted text, graph, interpreter, adapter or blocking conversion.
internal static class CanonicalAsyncHookReference
{
    internal static ValueTask<T> SendHookDirectResponse<R,T,K,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteDirectResponse<R,T,K,H>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteDirectResponse<R,T,K,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T>
    {
        token.ThrowIfCancellationRequested();
        var outer=provider.GetRequiredService<K>();
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            response=await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask SendHookDirectVoid<R,K,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteDirectVoid<R,K,H>(provider,request,token);
    }
    private static async ValueTask ExecuteDirectVoid<R,K,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R>
    {
        token.ThrowIfCancellationRequested();
        var outer=provider.GetRequiredService<K>();
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask<T> SendHookOwnedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R,T> where F:class,IOwnedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteOwnedResponse<R,T,K,H,F>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteOwnedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R,T> where F:class,IOwnedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=provider.GetRequiredService<F>().Create(request,token) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            response=await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
        }
        finally{var detached=outer;outer=null;await detached.DisposeAsync().ConfigureAwait(false);}
    }

    internal static ValueTask SendHookOwnedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R> where F:class,IOwnedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteOwnedVoid<R,K,H,F>(provider,request,token);
    }
    private static async ValueTask ExecuteOwnedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R> where F:class,IOwnedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=provider.GetRequiredService<F>().Create(request,token) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
        }
        finally{var detached=outer;outer=null;await detached.DisposeAsync().ConfigureAwait(false);}
    }

    internal static ValueTask<T> SendHookBorrowedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T> where F:class,IBorrowedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteBorrowedResponse<R,T,K,H,F>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteBorrowedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T> where F:class,IBorrowedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=provider.GetRequiredService<F>().Create(request,token) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            response=await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask SendHookBorrowedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R> where F:class,IBorrowedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteBorrowedVoid<R,K,H,F>(provider,request,token);
    }
    private static async ValueTask ExecuteBorrowedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R> where F:class,IBorrowedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=provider.GetRequiredService<F>().Create(request,token) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask<T> SendHookAsyncOwnedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R,T> where F:class,IAsyncOwnedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncOwnedResponse<R,T,K,H,F>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteAsyncOwnedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R,T> where F:class,IAsyncOwnedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=await provider.GetRequiredService<F>().CreateAsync(request,token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            response=await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
        }
        finally{var detached=outer;outer=null;await detached.DisposeAsync().ConfigureAwait(false);}
    }

    internal static ValueTask SendHookAsyncOwnedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R> where F:class,IAsyncOwnedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncOwnedVoid<R,K,H,F>(provider,request,token);
    }
    private static async ValueTask ExecuteAsyncOwnedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R>,IAsyncDisposable where H:class,IRequestHandler<R> where F:class,IAsyncOwnedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=await provider.GetRequiredService<F>().CreateAsync(request,token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        try
        {
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
        }
        finally{var detached=outer;outer=null;await detached.DisposeAsync().ConfigureAwait(false);}
    }

    internal static ValueTask<T> SendHookAsyncBorrowedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T> where F:class,IAsyncBorrowedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncBorrowedResponse<R,T,K,H,F>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteAsyncBorrowedResponse<R,T,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T> where F:class,IAsyncBorrowedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=await provider.GetRequiredService<F>().CreateAsync(request,token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            response=await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask SendHookAsyncBorrowedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R> where F:class,IAsyncBorrowedSessionFactory<R,K>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsyncBorrowedVoid<R,K,H,F>(provider,request,token);
    }
    private static async ValueTask ExecuteAsyncBorrowedVoid<R,K,H,F>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R> where F:class,IAsyncBorrowedSessionFactory<R,K>
    {
        token.ThrowIfCancellationRequested();
        K? outer=await provider.GetRequiredService<F>().CreateAsync(request,token).ConfigureAwait(false) ?? throw new InvalidOperationException("A session factory must return a nonnull Hook session.");
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedOuter,token).ConfigureAwait(false);
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask<T> SendHookTwoResponse<R,T,K,J,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where J:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTwoResponse<R,T,K,J,H>(provider,request,token);
    }
    private static async ValueTask<T> ExecuteTwoResponse<R,T,K,J,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest<T> where K:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where J:class,IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,T>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R,T>
    {
        token.ThrowIfCancellationRequested();
        var outer=provider.GetRequiredService<K>();
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            T response;
            var inner=provider.GetRequiredService<J>();var savedInner=savedOuter;var successInner=false;Exception? errorInner=null;
            try
            {
                savedInner=await inner.BeforeAsync(savedInner,token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(savedInner,"request");token.ThrowIfCancellationRequested();
            response=await provider.GetRequiredService<H>().HandleAsync(savedInner,token).ConfigureAwait(false);
            response=await inner.AfterAsync(savedInner,response,token).ConfigureAwait(false);
                successInner=true;
            }
            catch(Exception failure){errorInner=failure;try{await inner.OnExceptionAsync(savedInner,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorInner=replacement;throw;}throw;}
            finally{await inner.FinallyAsync(savedInner,new(successInner,errorInner),token).ConfigureAwait(false);}
            response=await outer.AfterAsync(savedOuter,response,token).ConfigureAwait(false);
            successOuter=true;
            return response;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

    internal static ValueTask SendHookTwoVoid<R,K,J,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where J:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R>
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTwoVoid<R,K,J,H>(provider,request,token);
    }
    private static async ValueTask ExecuteTwoVoid<R,K,J,H>(IServiceProvider provider,R request,CancellationToken token) where R:class,IRequest where K:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where J:class,IAsyncSendRequestTransform<R>,IAsyncSendSuccessObserver<R>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where H:class,IRequestHandler<R>
    {
        token.ThrowIfCancellationRequested();
        var outer=provider.GetRequiredService<K>();
        var savedOuter=request;var successOuter=false;Exception? errorOuter=null;
        try
        {
            savedOuter=await outer.BeforeAsync(savedOuter,token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(savedOuter,"request");token.ThrowIfCancellationRequested();
            var inner=provider.GetRequiredService<J>();var savedInner=savedOuter;var successInner=false;Exception? errorInner=null;
            try
            {
                savedInner=await inner.BeforeAsync(savedInner,token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(savedInner,"request");token.ThrowIfCancellationRequested();
            await provider.GetRequiredService<H>().HandleAsync(savedInner,token).ConfigureAwait(false);
            await inner.AfterAsync(savedInner,token).ConfigureAwait(false);
                successInner=true;
            }
            catch(Exception failure){errorInner=failure;try{await inner.OnExceptionAsync(savedInner,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorInner=replacement;throw;}throw;}
            finally{await inner.FinallyAsync(savedInner,new(successInner,errorInner),token).ConfigureAwait(false);}
            await outer.AfterAsync(savedOuter,token).ConfigureAwait(false);
            successOuter=true;
        }
        catch(Exception failure){errorOuter=failure;try{await outer.OnExceptionAsync(savedOuter,failure,token).ConfigureAwait(false);}catch(Exception replacement){errorOuter=replacement;throw;}throw;}
        finally{await outer.FinallyAsync(savedOuter,new(successOuter,errorOuter),token).ConfigureAwait(false);}
    }

}
