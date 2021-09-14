using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InsertTextIntoGSheet
{
    public readonly struct TradeResult
    {
        public TradeResult(string tier, string name, double price, string priceCell)
        {
            Tier = tier;
            Name = name;
            Price = price;
            PriceCell = priceCell;
        }

        public string Tier { get; init; }
        public string Name { get; init; }
        public double Price { get; init; }
        public string PriceCell { get; init; }

        public override string ToString() => $"{Tier}-{Name}: {Price}";
    }
}
