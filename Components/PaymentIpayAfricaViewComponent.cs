using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.IpayAfrica.Components
{
    [ViewComponent(Name = "PaymentIpayAfrica")]
    public class PaymentIpayAfricaViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.IpayAfrica/Views/PaymentInfo.cshtml");
        }
    }
}
