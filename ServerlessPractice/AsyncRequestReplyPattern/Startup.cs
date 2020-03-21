using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly : FunctionsStartup(typeof(AsyncRequestReplyPattern.Startup))]
namespace AsyncRequestReplyPattern
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton(x => new CloudStorageService());
        }
    }
}
