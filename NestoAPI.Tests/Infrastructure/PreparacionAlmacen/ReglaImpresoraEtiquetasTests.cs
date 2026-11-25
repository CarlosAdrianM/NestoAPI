using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Inmovilizados;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Carlos 25/11/26: LA REGLA común de la impresora de las etiquetas: (1) la pedida, validada; (2) la del almacén (fila
    /// «(defecto)» ImpresoraEtiquetas.{ALG|REI|ALC} o la de código); (3) sin almacén u otro, la del usuario.
    /// </summary>
    [TestClass]
    public class ReglaImpresoraEtiquetasTests
    {
        private static string SinFilas(string clave) => null;
        private static string NoDebeLlamarse() => throw new AssertFailedException("No debía mirar la impresora del usuario");

        [TestMethod]
        public void Pedida_Manda_SinEspacios()
        {
            ImpresoraEtiquetasElegida e = ReglaImpresoraEtiquetas.Elegir(@"  \\RDS2016\etiquetasreina ", "ALC", SinFilas, () => @"\\RDS2016\etiquetas2");

            Assert.AreEqual(@"\\RDS2016\etiquetasreina", e.Impresora);
            Assert.AreEqual(OrigenesImpresoraEtiquetas.PEDIDA, e.Origen);
        }

        [TestMethod]
        public void SinPedida_LaDelAlmacen_DeCodigoSinFila()
        {
            ImpresoraEtiquetasElegida alg = ReglaImpresoraEtiquetas.Elegir(null, "alg", SinFilas, NoDebeLlamarse);
            ImpresoraEtiquetasElegida rei = ReglaImpresoraEtiquetas.Elegir(" ", "REI ", _ => "  ", NoDebeLlamarse);
            ImpresoraEtiquetasElegida alc = ReglaImpresoraEtiquetas.Elegir("", "ALC", SinFilas, NoDebeLlamarse);

            Assert.AreEqual(@"\\RDS2016\etiquetas2", alg.Impresora);
            Assert.AreEqual(@"\\RDS2016\etiquetasreina", rei.Impresora);
            Assert.AreEqual(@"\\RDS2016\etiquetasalcobendas", alc.Impresora);
            Assert.AreEqual(OrigenesImpresoraEtiquetas.ALMACEN, alc.Origen);
        }

        [TestMethod]
        public void SinPedida_ConFilaGeneral_Esa()
        {
            var leidas = new List<string>();
            ImpresoraEtiquetasElegida e = ReglaImpresoraEtiquetas.Elegir(null, "ALC", c => { leidas.Add(c); return @" \\RDS2016\zebraalc "; }, NoDebeLlamarse);

            Assert.AreEqual(@"\\RDS2016\zebraalc", e.Impresora);
            CollectionAssert.AreEqual(new[] { "ImpresoraEtiquetas.ALC" }, leidas);
        }

        [TestMethod]
        public void SinAlmacenUOtro_LaDelUsuario_SinLeerParametros()
        {
            Func<string, string> prohibido = _ => throw new AssertFailedException("No debía leer parámetros");

            ImpresoraEtiquetasElegida sinAlmacen = ReglaImpresoraEtiquetas.Elegir(null, null, prohibido, () => @"\\RDS2016\etiquetas3");
            ImpresoraEtiquetasElegida otro = ReglaImpresoraEtiquetas.Elegir(null, "VAR", prohibido, () => @"\\RDS2016\etiquetas3");

            Assert.AreEqual(@"\\RDS2016\etiquetas3", sinAlmacen.Impresora);
            Assert.AreEqual(OrigenesImpresoraEtiquetas.USUARIO, sinAlmacen.Origen);
            Assert.AreEqual(@"\\RDS2016\etiquetas3", otro.Impresora);
        }

        [TestMethod]
        public void SinAlmacen_UsuarioSinImpresora_ElMotivoDeSiempre()
        {
            ArgumentException ex = Assert.ThrowsException<ArgumentException>(() =>
                ReglaImpresoraEtiquetas.Elegir(null, null, SinFilas, () => throw new ArgumentException("El usuario X no tiene impresora")));
            StringAssert.Contains(ex.Message, "no tiene impresora");
        }

        // ---- Inmovilizados: la clave antigua gana sobre la general ----

        [TestMethod]
        public void Inmovilizado_ClaveAntiguaGanaSobreLaGeneral()
        {
            var filas = new Dictionary<string, string>
            {
                ["ImpresoraEtiquetasInmovilizado.REI"] = @"\\RDS2016\antigua",
                ["ImpresoraEtiquetas.REI"] = @"\\RDS2016\general"
            };

            Assert.AreEqual(@"\\RDS2016\antigua", ReglaImpresoraInmovilizado.Elegir(null, "REI", c => filas.TryGetValue(c, out string v) ? v : null, NoDebeLlamarse));
        }

        [TestMethod]
        public void Inmovilizado_SinClaveAntigua_LaGeneral()
        {
            var filas = new Dictionary<string, string> { ["ImpresoraEtiquetas.ALG"] = @"\\RDS2016\general" };

            Assert.AreEqual(@"\\RDS2016\general", ReglaImpresoraInmovilizado.Elegir(null, "ALG", c => filas.TryGetValue(c, out string v) ? v : null, NoDebeLlamarse));
        }

        // ---- La pedida se valida ----

        [TestMethod]
        public void Valida_RutasBuenas()
        {
            Assert.IsNull(ReglaImpresoraEtiquetas.MotivoNoValida(@"\\RDS2016\etiquetas2", null));
            Assert.IsNull(ReglaImpresoraEtiquetas.MotivoNoValida(@"\\rds2016\Etiquetas (Reina)", null), "El servidor no distingue mayúsculas");
            Assert.IsNull(ReglaImpresoraEtiquetas.MotivoNoValida(@"\\impresoras.nuevavision.local\zebra_alc", new[] { @"\\impresoras.nuevavision.local\otra" }));
        }

        [TestMethod]
        public void Valida_RutasQueNoValen()
        {
            string[] malas =
            {
                "etiquetas2",                         // sin servidor
                @"C:\Windows\cola",                   // local
                @"\\RDS2016",                         // sin cola
                @"\\RDS2016\cola\otra",               // con más barras
                @"\\RDS2016\..",                      // salir de la ruta
                @"\\RDS2016\cola;del",                // caracteres raros
                @"\\RDS2016\co" + "\t" + "la",        // control
                @"\\RDS2016\cola|x",
                "\\\\RDS2016\\" + new string('a', 120) // demasiado larga
            };
            foreach (string mala in malas)
            {
                Assert.IsNotNull(ReglaImpresoraEtiquetas.MotivoNoValida(mala, null), mala);
            }
        }

        [TestMethod]
        public void Valida_ServidorDesconocido_NoVale()
        {
            string motivo = ReglaImpresoraEtiquetas.MotivoNoValida(@"\\10.0.0.66\cola", new[] { @"\\RDS2016\etiquetas2" });

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "RDS2016");
            Assert.ThrowsException<ArgumentException>(() => ReglaImpresoraEtiquetas.Elegir(@"\\otro-servidor\cola", "ALG", SinFilas, () => @"\\RDS2016\etiquetas2"));
        }

        [TestMethod]
        public void Valida_ElServidorDeLaFilaODelUsuario_Vale()
        {
            ImpresoraEtiquetasElegida porFila = ReglaImpresoraEtiquetas.Elegir(@"\\PRINTSRV\cola", "ALG",
                c => c == "ImpresoraEtiquetas.ALC" ? @"\\PRINTSRV\zebra" : null, () => throw new ArgumentException("sin impresora"));
            ImpresoraEtiquetasElegida porUsuario = ReglaImpresoraEtiquetas.Elegir(@"\\PCALMACEN\cola", null, SinFilas, () => @"\\PCALMACEN\zebra");

            Assert.AreEqual(@"\\PRINTSRV\cola", porFila.Impresora);
            Assert.AreEqual(@"\\PCALMACEN\cola", porUsuario.Impresora);
        }

        // ---- La lista para elegir ----

        [TestMethod]
        public void Lista_LasTres_YLaDelUsuarioSiEsOtra()
        {
            List<ImpresoraEtiquetasDTO> lista = ReglaImpresoraEtiquetas.Lista(SinFilas, () => @"\\RDS2016\etiquetas3");

            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC", null }, lista.Select(i => i.Almacen).ToList());
            CollectionAssert.AreEqual(new[] { "Algete", "Reina", "Alcobendas", "La suya" }, lista.Select(i => i.Nombre).ToList());
            Assert.AreEqual(@"\\RDS2016\etiquetas3", lista.Single(i => i.EsLaDelUsuario).Impresora);
        }

        [TestMethod]
        public void Lista_LaDelUsuarioEsLaDeUnAlmacen_SeMarcaSinRepetir()
        {
            List<ImpresoraEtiquetasDTO> lista = ReglaImpresoraEtiquetas.Lista(SinFilas, () => @"\\rds2016\ETIQUETAS2 ");

            Assert.AreEqual(3, lista.Count);
            Assert.AreEqual("ALG", lista.Single(i => i.EsLaDelUsuario).Almacen);
        }

        [TestMethod]
        public void Lista_UsuarioSinImpresora_SoloLasTres()
        {
            List<ImpresoraEtiquetasDTO> lista = ReglaImpresoraEtiquetas.Lista(SinFilas, () => throw new ArgumentException("sin impresora"));

            Assert.AreEqual(3, lista.Count);
            Assert.IsFalse(lista.Any(i => i.EsLaDelUsuario));
        }
    }
}
