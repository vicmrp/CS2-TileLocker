using System;
using System.Linq;
using CS2_TileLocker;

static class Program
{
    static void Main()
    {
        void Check(double[] weights, int paid)
        {
            int[] amounts = TileRefundMath.Allocate(weights, paid);
            if (amounts.Any(x => x < 0) || amounts.Sum(x => (long)x) != paid)
                throw new Exception("Invoice allocation lost or created money.");
        }
        Check(new double[] { 1234 }, 1234);
        Check(new double[] { 100.49, 100.49, 100.49 }, 301);
        Check(new double[] { 1, 1, 1 }, 2);
        Check(new double[] { 0, 5, 0 }, 500);
        Check(new double[] { 900000000, 1000000000 }, 1900000000);
        Check(new double[] { 12, 25 }, 0);
        if (TileRefundMath.Allocate(new double[] { 0, 0 }, 0).Any(x => x != 0)) throw new Exception("Free tiles refunded.");
        var random = new Random(42);
        for (int n = 0; n < 10000; n++)
        {
            var weights = Enumerable.Range(0, random.Next(1, 530)).Select(_ => random.NextDouble() * 10000000).ToArray();
            Check(weights, random.Next(0, 2000000000));
        }
        Console.WriteLine("PASS: 10,007 refund scenarios preserve the exact invoice total, including rounding, free purchases and large amounts.");
    }
}
