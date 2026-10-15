using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Outlet;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Outlet
{
    /// <summary>NestoAPI#643: la regla del Outlet (estado 4 + en la tienda) y su categoría por grupo/subgrupo.</summary>
    [TestClass]
    public class ReglasOutletTests
    {
        internal static SituacionOutletProducto Producto(string numero, string grupo, string subgrupo, short estado = 4,
            bool ficha = true, bool publicable = true, CategoriaOutlet[] actuales = null)
        {
            return new SituacionOutletProducto
            {
                Numero = numero,
                Nombre = "PRODUCTO " + numero,
                Grupo = grupo,
                Subgrupo = subgrupo,
                Estado = estado,
                TieneFichaWeb = ficha,
                Publicable = publicable,
                MotivoNoPublicable = publicable ? null : "es un producto ficticio",
                CategoriasOutletActuales = (actuales ?? new CategoriaOutlet[0]).ToList()
            };
        }

        [DataTestMethod]
        [DataRow("PEL", "TIN", "PEL/OUT")]
        [DataRow("PEL", "201", "PEL/OUT")]
        [DataRow("COS", "201", "COS/OUM")]
        [DataRow("COS", "202", "COS/OUM")]
        [DataRow("COS", "MAQ", "COS/OUM")]
        [DataRow("ACC", "004", "COS/OUM")]
        [DataRow("ACC", "005", "COS/OUM")]
        [DataRow("ACC", "006", "COS/OUM")]
        [DataRow("COS", "124", "COS/OUT")]
        [DataRow("ACC", "001", "COS/OUT")]
        [DataRow("ACC", "999", "COS/OUT")]
        [DataRow("APA", "007", "COS/OUT")]
        [DataRow("ACP", "001", "COS/OUT")]
        [DataRow("pel ", "tin", "PEL/OUT")]
        [DataRow("cos", "maq ", "COS/OUM")]
        public void CategoriaQueToca_SegunGrupoYSubgrupo(string grupo, string subgrupo, string esperada)
        {
            Assert.AreEqual(esperada, ReglasOutlet.CategoriaQueToca(grupo, subgrupo).ToString());
        }

        [TestMethod]
        public void EsCategoriaOutlet_SoloLasTres()
        {
            Assert.IsTrue(ReglasOutlet.EsCategoriaOutlet("PEL", "OUT"));
            Assert.IsTrue(ReglasOutlet.EsCategoriaOutlet("COS", "OUM"));
            Assert.IsTrue(ReglasOutlet.EsCategoriaOutlet("COS ", "OUT "));
            Assert.IsFalse(ReglasOutlet.EsCategoriaOutlet("PEL", "OUM"));
            Assert.IsFalse(ReglasOutlet.EsCategoriaOutlet("COS", "OFE"));
        }

        [TestMethod]
        public void Calcular_Estado4EnTiendaSinOutlet_Entra()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[] { Producto("100", "COS", "201") });

            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual(TiposCambioOutlet.ENTRA, cambios[0].Tipo);
            Assert.AreEqual("COS/OUM", cambios[0].Poner);
            Assert.AreEqual(0, cambios[0].Quitar.Count);
            Assert.IsTrue(cambios[0].Republicar);
        }

        [TestMethod]
        public void Calcular_Estado4SinFichaWeb_NoEntraNiSePublica()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[] { Producto("100", "PEL", "TIN", ficha: false) });

            Assert.AreEqual(0, cambios.Count, "Lo que no está en la tienda no entra nunca en el Outlet");
        }

        [TestMethod]
        public void Calcular_Estado4QueLaPuertaNoDejaPublicar_NoEntra()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[] { Producto("100", "PEL", "TIN", publicable: false) });

            Assert.AreEqual(0, cambios.Count);
        }

        [TestMethod]
        public void Calcular_EnOutletSinEstado4_SaleDeTodas()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("200", "COS", "124", estado: 0, actuales: new[] { ReglasOutlet.ESTETICA, ReglasOutlet.MAQUILLAJE_Y_UNAS })
            });

            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual(TiposCambioOutlet.SALE, cambios[0].Tipo);
            Assert.IsNull(cambios[0].Poner);
            CollectionAssert.AreEquivalent(new[] { "COS/OUT", "COS/OUM" }, cambios[0].Quitar);
            StringAssert.Contains(cambios[0].Motivo, "estado 0");
        }

        [TestMethod]
        public void Calcular_EnOutletEstado4SinFicha_SaleSinRepublicar()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("201", "PEL", "TIN", ficha: false, actuales: new[] { ReglasOutlet.PELUQUERIA })
            });

            Assert.AreEqual(TiposCambioOutlet.SALE, cambios.Single().Tipo);
            Assert.IsFalse(cambios.Single().Republicar, "Sin ficha web no se republica: no está en la tienda");
        }

        [TestMethod]
        public void Calcular_EnLaCategoriaQueNoToca_CambiaDeGrupo()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("300", "ACC", "005", actuales: new[] { ReglasOutlet.ESTETICA })
            });

            Assert.AreEqual(TiposCambioOutlet.CAMBIA_DE_GRUPO, cambios.Single().Tipo);
            Assert.AreEqual("COS/OUM", cambios.Single().Poner);
            CollectionAssert.AreEqual(new[] { "COS/OUT" }, cambios.Single().Quitar);
        }

        [TestMethod]
        public void Calcular_EnLaSuyaYEnOtraMas_QuitaLaQueSobraSinPonerNada()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("301", "PEL", "TIN", actuales: new[] { ReglasOutlet.PELUQUERIA, ReglasOutlet.ESTETICA })
            });

            Assert.AreEqual(TiposCambioOutlet.CAMBIA_DE_GRUPO, cambios.Single().Tipo);
            Assert.IsNull(cambios.Single().Poner);
            CollectionAssert.AreEqual(new[] { "COS/OUT" }, cambios.Single().Quitar);
        }

        [TestMethod]
        public void Calcular_YaEnLaSuya_NoCambia()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("400", "PEL", "TIN", actuales: new[] { ReglasOutlet.PELUQUERIA }),
                Producto("401", "COS", "OFE", estado: 1) // ni está ni debe estar
            });

            Assert.AreEqual(0, cambios.Count);
        }

        [TestMethod]
        public void Calcular_Ejemplo_CuentaEntranSalenYCambian()
        {
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(new[]
            {
                Producto("1", "PEL", "TIN"),                                                    // entra PEL/OUT
                Producto("2", "COS", "MAQ"),                                                    // entra COS/OUM
                Producto("3", "APA", "001"),                                                    // entra COS/OUT
                Producto("4", "COS", "124", estado: 1, actuales: new[] { ReglasOutlet.ESTETICA }),        // sale
                Producto("5", "PEL", "TIN", estado: -1, actuales: new[] { ReglasOutlet.PELUQUERIA }),     // sale (de baja)
                Producto("6", "COS", "201", actuales: new[] { ReglasOutlet.ESTETICA }),                   // cambia a COS/OUM
                Producto("7", "COS", "124", actuales: new[] { ReglasOutlet.ESTETICA }),                   // se queda
                Producto("8", "COS", "124", ficha: false)                                       // no está en la tienda
            });

            Assert.AreEqual(3, cambios.Count(c => c.Tipo == TiposCambioOutlet.ENTRA));
            Assert.AreEqual(2, cambios.Count(c => c.Tipo == TiposCambioOutlet.SALE));
            Assert.AreEqual(1, cambios.Count(c => c.Tipo == TiposCambioOutlet.CAMBIA_DE_GRUPO));
            CollectionAssert.AreEquivalent(new[] { "1", "2", "3", "4", "5", "6" }, cambios.Select(c => c.Numero).ToList());
            Assert.AreEqual("COS/OUT", cambios.Single(c => c.Numero == "3").Poner);
        }

        [TestMethod]
        public void EnLaTienda_TambienLoPublicadoPorNestoSync()
        {
            // Carlos 15/10/26: 66 de estado 4 sin ficha web estaban en el Outlet y en la tienda (carga de #414); cuentan como «en la tienda».
            StringAssert.Contains(RepositorioOutletSql.SQL_PUBLICADOS, "FROM dbo.Nesto_sync");
            StringAssert.Contains(RepositorioOutletSql.SQL_PUBLICADOS, "Tabla = 'Productos' AND Sincronizado IS NOT NULL");
        }
    }
}
