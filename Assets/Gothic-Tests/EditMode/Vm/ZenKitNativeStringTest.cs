using System.Text;
using Gothic.Core.Extensions;
using NUnit.Framework;

namespace Gothic.Tests.EditMode.Vm
{
    /// <summary>
    /// Strings handed to the Daedalus VM must be bytes in the installation's code page, one byte per character.
    /// IL2CPP sends UTF-8 through ZenKit's own string calls, which scrambled umlauts in script-built texts
    /// (a trainer's "Einhänder ..." choice on Quest).
    /// </summary>
    public class ZenKitNativeStringTest
    {
        private const int _westEurope = 1252;
        private const int _centralEurope = 1250;
        private const int _eastEurope = 1251;

        [TestCase("Einhänder", _westEurope, 4, 0xE4)]
        [TestCase("ZURÜCK", _westEurope, 3, 0xDC)]
        [TestCase("łatwy", _centralEurope, 0, 0xB3)]
        [TestCase("Ящик", _eastEurope, 0, 0xDF)]
        public void NonAsciiCharacterIsOneByteOfTheCodePage(string text, int codePage, int index, int expectedByte)
        {
            var bytes = ZenKitVmExtension.ToNativeString(text, Encoding.GetEncoding(codePage));

            Assert.AreEqual(text.Length + 1, bytes.Length, "one byte per character plus the terminator");
            Assert.AreEqual(expectedByte, bytes[index]);
        }

        [TestCase("Einhänder Stufe 1 (50 Erz, 10 Lernpunkte)", _westEurope)]
        [TestCase("plain ascii", _westEurope)]
        public void EndsWithExactlyOneZero(string text, int codePage)
        {
            var bytes = ZenKitVmExtension.ToNativeString(text, Encoding.GetEncoding(codePage));

            Assert.AreEqual(0, bytes[^1]);
            for (var i = 0; i < bytes.Length - 1; i++)
                Assert.AreNotEqual(0, bytes[i], $"byte {i} would end the C string early");
        }

        [Test]
        public void NullAndEmptyAreAnEmptyCString()
        {
            var encoding = Encoding.GetEncoding(_westEurope);

            Assert.AreEqual(new byte[] { 0 }, ZenKitVmExtension.ToNativeString(null, encoding));
            Assert.AreEqual(new byte[] { 0 }, ZenKitVmExtension.ToNativeString(string.Empty, encoding));
        }

        [Test]
        public void Utf8WouldScrambleIt()
        {
            // What IL2CPP sends through ZenKit's marshaller, read back in the game's code page: the bug.
            var utf8 = Encoding.UTF8.GetBytes("Einhänder");
            var scrambled = Encoding.GetEncoding(_westEurope).GetString(utf8);

            Assert.AreNotEqual("Einhänder", scrambled);
            Assert.AreEqual("Einhänder".Length + 1, scrambled.Length);
        }
    }
}
