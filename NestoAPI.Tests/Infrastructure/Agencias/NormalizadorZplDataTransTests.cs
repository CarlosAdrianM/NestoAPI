using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Normalizador del ZPL de DataTrans: convierte los acentos hex Latin-1 (_e1_) a hex UTF-8
    /// (_c3_a1_) cuando el ZPL está en ^CI28, para que la Zebra no se "coma" las tildes.
    /// </summary>
    [TestClass]
    public class NormalizadorZplDataTransTests
    {
        [TestMethod]
        public void Convierte_ByteLatin1_AHexUtf8_ConCI28()
        {
            // "Rápida" con la á como _e1_ (Latin-1) -> _c3_a1_ (UTF-8).
            string zpl = "^XA^CI28^FH^FD_52_e1_70_69_64_61^FS^XZ";

            string resultado = NormalizadorZplDataTrans.CorregirAcentos(zpl);

            Assert.AreEqual("^XA^CI28^FH^FD_52_c3_a1_70_69_64_61^FS^XZ", resultado);
        }

        [TestMethod]
        public void NoTocaLosBytesAscii()
        {
            // Todos los tokens < 0x80: se queda igual.
            string zpl = "^XA^CI28^FH^FD_52_70_69^FS^XZ";

            Assert.AreEqual(zpl, NormalizadorZplDataTrans.CorregirAcentos(zpl));
        }

        [TestMethod]
        public void ConvierteVariosAcentos()
        {
            // É=_c9_->_c3_89_, Ñ=_d1_->_c3_91_, ó=_f3_->_c3_b3_
            string zpl = "^CI28^FH^FD_c9_d1_f3^FS";

            string resultado = NormalizadorZplDataTrans.CorregirAcentos(zpl);

            Assert.AreEqual("^CI28^FH^FD_c3_89_c3_91_c3_b3^FS", resultado);
        }

        [TestMethod]
        public void SinCI28_NoTocaNada()
        {
            // Si no está en modo UTF-8, no sabemos el code page: no se convierte.
            string zpl = "^XA^FH^FD_52_e1_70^FS^XZ";

            Assert.AreEqual(zpl, NormalizadorZplDataTrans.CorregirAcentos(zpl));
        }

        [TestMethod]
        public void NuloOVacio_DevuelveLoMismo()
        {
            Assert.IsNull(NormalizadorZplDataTrans.CorregirAcentos(null));
            Assert.AreEqual(string.Empty, NormalizadorZplDataTrans.CorregirAcentos(string.Empty));
        }
    }
}
