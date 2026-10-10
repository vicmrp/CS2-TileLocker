using System;

namespace CS2_TileLocker
{
    internal static class TileRefundMath
    {
        // Allocate the actual invoice, including the game's single rounding step.
        // Cumulative rounding keeps the sum exact for multi-tile purchases.
        internal static int[] Allocate(double[] weights, int paid)
        {
            var result = new int[weights.Length];
            double total = 0;
            foreach (double weight in weights) total += Math.Max(0, weight);
            if (paid <= 0 || total <= 0 || double.IsInfinity(total)) return result;
            double cumulative = 0;
            int allocated = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += Math.Max(0, weights[i]);
                int through = i == weights.Length - 1 ? paid : (int)Math.Round(paid * cumulative / total);
                result[i] = through - allocated;
                allocated = through;
            }
            return result;
        }
    }
}
