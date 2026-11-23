using System;
using System.Collections.Generic;
using System.Linq;
using RogueStars.Rules.Content;

namespace RogueStars.Sim
{
    public static class SimContent
    {
        /// <summary>Default content in offline mode (dev receipts accepted: purchases go through PurchaseComplete).</summary>
        public static ContentDb Create()
        {
            var c = new ContentDb();
            c.Shop.AllowDevReceipts = true;
            return c;
        }
    }
}
