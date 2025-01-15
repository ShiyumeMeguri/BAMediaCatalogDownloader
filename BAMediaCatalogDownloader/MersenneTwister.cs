using K4os.Hash.xxHash;
using System.Text;
// 梅森旋转算法
namespace GameMainConfigEncryption
{
    public class MersenneTwister
    {
        private const int N = 624;
        private const int M = 397;
        private const uint MATRIX_A = 0x9908B0DF;
        private const uint UPPER_MASK = 0x80000000;
        private const uint LOWER_MASK = 0x7FFFFFFF;

        private readonly uint[] mt = new uint[N];
        private int mti = N + 1;

        private static readonly uint[] mag01 = { 0x0U, MATRIX_A }; // Precomputed mag01 array

        public MersenneTwister(uint seed)
        {
            mt[0] = seed & 0xFFFFFFFFU;
            for (mti = 1; mti < N; mti++)
            {
                mt[mti] = (uint)((1812433253U * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + mti) & 0xFFFFFFFFU);
            }
        }

        private void GenerateNumbers()
        {
            int i;
            for (i = 0; i < N - M; i++)
            {
                uint y = (mt[i] & UPPER_MASK) | (mt[i + 1] & LOWER_MASK);
                mt[i] = mt[i + M] ^ (y >> 1) ^ mag01[y & 0x1];
            }
            for (; i < N - 1; i++)
            {
                uint y = (mt[i] & UPPER_MASK) | (mt[i + 1] & LOWER_MASK);
                mt[i] = mt[i + (M - N)] ^ (y >> 1) ^ mag01[y & 0x1];
            }
            uint yFinal = (mt[N - 1] & UPPER_MASK) | (mt[0] & LOWER_MASK);
            mt[N - 1] = mt[M - 1] ^ (yFinal >> 1) ^ mag01[yFinal & 0x1];

            mti = 0;
        }

        public uint GenRandInt32()
        {
            if (mti >= N)
            {
                if (mti == N + 1)
                    Initialize(5489U); // Default seed
                GenerateNumbers();
            }

            uint y = mt[mti++];
            y ^= (y >> 11);
            y ^= (y << 7) & 0x9D2C5680U;
            y ^= (y << 15) & 0xEFC60000U;
            y ^= (y >> 18);
            return y;
        }
        public byte[] NextBytes(int length)
        {
            byte[] result = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                uint val31 = GenRandInt32() >> 1;

                byte[] chunk = BitConverter.GetBytes(val31);

                for (int i = 0; i < 4 && offset < length; i++)
                {
                    result[offset++] = chunk[i];
                }
            }

            return result;
        }

        private void Initialize(uint seed)
        {
            mt[0] = seed & 0xFFFFFFFFU;
            for (mti = 1; mti < N; mti++)
            {
                mt[mti] = (uint)((1812433253U * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + mti) & 0xFFFFFFFFU);
            }
        }
    }
}
