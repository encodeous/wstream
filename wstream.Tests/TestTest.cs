using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace wstream.Tests
{
    public class TestTest
    {
        [Theory]
        [MemberData(nameof(TestData))]
        public void AdditionTest(int a, int b, int c)
        {
            Assert.Equal(a, b + c);
        }

        public static IEnumerable<object[]> TestData()
        {
            var rng = new Random();
            for (int i = 0; i < 10000; i++)
            {
                int b = rng.Next(-100000, 100000), c = rng.Next(-100000, 100000);
                yield return new object[] {b + c, b, c};
            }
        }
    }
}