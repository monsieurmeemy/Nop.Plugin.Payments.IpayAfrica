using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.IpayAfrica
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            routeBuilder.MapRoute("Plugin.Payments.IpayAfrica.Return",
                 "Plugins/PaymentIpayAfrica/Return",
                 new { controller = "PaymentIpayAfrica", action = "Return" });
        }

        public int Priority
        {
            get { return -1; }
        }
    }
}
