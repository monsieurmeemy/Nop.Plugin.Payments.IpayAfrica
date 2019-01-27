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

            /*routeBuilder.MapRoute("Plugin.Payments.IpayAfrica.Return",
                "Plugins/PaymentIpayAfrica/Return",
                new { controller = "PaymentIpayAfrica", action = "Return" },
                new[] { "Nop.Plugin.Payments.IpayAfrica.Controllers" });*/
        }

        public int Priority
        {
            get { return -1; }
        }
    }
}
