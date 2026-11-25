using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class ReglaOfertasParaTiTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private const string BF = "Black Friday 2026";

        private static FilaCampanaOferta Fila(string producto, decimal descuento = 0.30m, string campana = BF, byte audiencia = 2,
            string familia = "EVA", string grupo = "COS", string subgrupo = "CRE", short? estado = 0, DateTime? desde = null, DateTime? hasta = null)
            => new FilaCampanaOferta
            {
                Campana = campana,
                Producto = producto,
                Descuento = descuento,
                AudienciaOferta = audiencia,
                FechaDesde = desde ?? new DateTime(2026, 11, 25),
                FechaHasta = hasta ?? new DateTime(2026, 11, 30),
                Nombre = "PRODUCTO " + producto,
                Familia = familia,
                NombreFamilia = "EVA VISNÚ",
                Grupo = grupo,
                SubGrupo = subgrupo,
                PVP = 10,
                Estado = estado
            };

        private static CandidataOferta Candidata(string producto, string familia = "EVA", decimal descuento = 0.30m, int popularidad = 0)
            => new CandidataOferta
            {
                Producto = producto,
                Nombre = "PRODUCTO " + producto,
                Familia = familia,
                NombreFamilia = familia == "EVA" ? "EVA VISNÚ" : familia,
                Campana = BF,
                Descuento = descuento,
                FechaDesde = new DateTime(2026, 11, 25),
                FechaHasta = new DateTime(2026, 11, 30),
                Popularidad = popularidad
            };

        private static CompraDiaReposicion Compra(string producto, DateTime dia, int cantidad = 1, decimal importe = 10)
            => new CompraDiaReposicion { Cliente = "1000", Producto = producto, Dia = dia, Cantidad = cantidad, BaseImponible = importe };

        [TestMethod]
        public void Maximo_PorDefecto5_Entre1y10()
        {
            Assert.AreEqual(5, ReglaOfertasParaTi.Maximo(null));
            Assert.AreEqual(1, ReglaOfertasParaTi.Maximo(0));
            Assert.AreEqual(10, ReglaOfertasParaTi.Maximo(50));
            Assert.AreEqual(3, ReglaOfertasParaTi.Maximo(3));
        }

        [TestMethod]
        public void Candidatas_SoloVigentesDeProfesionalVivasYSinMuestrasNiMateriasPrimas()
        {
            List<CandidataOferta> c = ReglaOfertasParaTi.Candidatas(new[]
            {
                Fila("1"),
                Fila("2", audiencia: 0),
                Fila("3", audiencia: 3),
                Fila("4", subgrupo: "MMP"),
                Fila("5", grupo: "MTP"),
                Fila("6", estado: -1),
                Fila("7", estado: null),
                Fila("8", hasta: new DateTime(2026, 11, 25)),
                Fila("9", desde: new DateTime(2026, 11, 27)),
                Fila("10", descuento: 0),
                Fila("11", audiencia: 1)
            }, BF, HOY);

            CollectionAssert.AreEquivalent(new[] { "1", "11" }, c.Select(x => x.Producto).ToList());
        }

        [TestMethod]
        public void Candidatas_ProductoEnDosCampanas_LaDeMasDescuento()
        {
            List<CandidataOferta> c = ReglaOfertasParaTi.Candidatas(new[]
            {
                Fila("1", 0.20m, "Otra"),
                Fila("1", 0.35m, BF)
            }, null, HOY);

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(BF, c[0].Campana);
            Assert.AreEqual(0.35m, c[0].Descuento);
        }

        [TestMethod]
        public void Candidatas_ConCampanaPedida_SoloEsa()
        {
            List<CandidataOferta> c = ReglaOfertasParaTi.Candidatas(new[] { Fila("1", 0.50m, "Otra"), Fila("2") }, "black friday 2026", HOY);
            Assert.AreEqual("2", c.Single().Producto);
        }

        [TestMethod]
        public void DescuentoDeFila_ConPrecioFijo_ContraElPvp()
        {
            var fila = Fila("1", 0);
            fila.Precio = 7.5m;
            Assert.AreEqual(0.25m, ReglaOfertasParaTi.DescuentoDeFila(fila));
        }

        [TestMethod]
        public void Evaluar_FueraLoPendienteYLoPedidoDesdeQueEmpezo()
        {
            var historial = new HistorialClienteOfertas
            {
                Compras = new List<CompraDiaReposicion> { Compra("2", new DateTime(2026, 11, 25)), Compra("3", new DateTime(2026, 11, 20)) },
                Pendientes = new HashSet<string> { "1" }
            };
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[] { Candidata("1"), Candidata("2"), Candidata("3") }, historial, HOY);

            CollectionAssert.AreEqual(new[] { "3" }, e.Select(x => x.Candidata.Producto).ToList());
        }

        [TestMethod]
        public void Evaluar_HabitualQueLeToca_TeTocaConSuTexto()
        {
            // cada 45 días, la última hace 62
            DateTime ultima = HOY.AddDays(-62);
            var historial = new HistorialClienteOfertas
            {
                Compras = new List<CompraDiaReposicion> { Compra("1", ultima.AddDays(-90), 2), Compra("1", ultima.AddDays(-45), 2), Compra("1", ultima, 2) }
            };
            CandidataEvaluada e = ReglaOfertasParaTi.Evaluar(new[] { Candidata("1") }, historial, HOY).Single();

            Assert.AreEqual(MotivosOfertaParaTi.TE_TOCA, e.Motivo);
            Assert.AreEqual("Lo pides cada 45 días y llevas 62 sin pedirlo", e.Texto);
            Assert.AreEqual(2, e.CantidadSugerida);
            Assert.IsTrue(e.Habitual);
        }

        [TestMethod]
        public void Evaluar_HabitualQueNoLeToca_LoSuelesPedir()
        {
            var historial = new HistorialClienteOfertas
            {
                Compras = new List<CompraDiaReposicion> { Compra("1", HOY.AddDays(-65)), Compra("1", HOY.AddDays(-35)), Compra("1", HOY.AddDays(-5)) }
            };
            CandidataEvaluada e = ReglaOfertasParaTi.Evaluar(new[] { Candidata("1") }, historial, HOY).Single();

            Assert.AreEqual(MotivosOfertaParaTi.LO_SUELES_PEDIR, e.Motivo);
            Assert.AreEqual("Lo has pedido 3 veces en el último año", e.Texto);
        }

        [TestMethod]
        public void Evaluar_DeUnaFamiliaQueCompra_DeTusMarcas()
        {
            var historial = new HistorialClienteOfertas
            {
                Compras = new List<CompraDiaReposicion> { Compra("99", HOY.AddDays(-100), importe: 300) },
                FamiliaDe = new Dictionary<string, string> { { "99", "EVA" } }
            };
            CandidataEvaluada e = ReglaOfertasParaTi.Evaluar(new[] { Candidata("1", "EVA") }, historial, HOY).Single();

            Assert.AreEqual(MotivosOfertaParaTi.DE_TUS_MARCAS, e.Motivo);
            Assert.AreEqual("De Eva Visnú, que sueles comprar", e.Texto);
        }

        [TestMethod]
        public void Evaluar_SinHistorial_TePuedeInteresar_PorPopularidad()
        {
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[]
            {
                Candidata("1", "A", popularidad: 5),
                Candidata("2", "B", popularidad: 50)
            }, new HistorialClienteOfertas(), HOY);

            CollectionAssert.AreEqual(new[] { "2", "1" }, e.Select(x => x.Candidata.Producto).ToList());
            Assert.AreEqual(MotivosOfertaParaTi.TE_PUEDE_INTERESAR, e[0].Motivo);
            Assert.AreEqual("De lo más pedido por otros profesionales", e[0].Texto);
            Assert.AreEqual("Una oferta que te puede interesar", e[1].Texto);
        }

        [TestMethod]
        public void Evaluar_OrdenDeLosGrupos_TocaHabitualMarcasResto()
        {
            var historial = new HistorialClienteOfertas
            {
                Compras = new List<CompraDiaReposicion>
                {
                    Compra("TOCA", HOY.AddDays(-100)), Compra("TOCA", HOY.AddDays(-70)),
                    Compra("HAB", HOY.AddDays(-20)), Compra("HAB", HOY.AddDays(-2)),
                    Compra("X", HOY.AddDays(-200), importe: 500)
                },
                FamiliaDe = new Dictionary<string, string> { { "X", "MARCA" } }
            };
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[]
            {
                Candidata("RESTO", "OTRA", popularidad: 999),
                Candidata("MARCA1", "MARCA"),
                Candidata("HAB", "F1"),
                Candidata("TOCA", "F2")
            }, historial, HOY);

            CollectionAssert.AreEqual(new[] { "TOCA", "HAB", "MARCA1", "RESTO" }, e.Select(x => x.Candidata.Producto).ToList());
        }

        [TestMethod]
        public void Elegir_ComoMuchoDosDeLaMismaFamiliaSiHayAlternativas()
        {
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[]
            {
                Candidata("1", "A", popularidad: 10), Candidata("2", "A", popularidad: 9), Candidata("3", "A", popularidad: 8),
                Candidata("4", "B", popularidad: 1)
            }, new HistorialClienteOfertas(), HOY);

            List<OfertaElegida> elegidas = ReglaOfertasParaTi.Elegir(e, 3);
            CollectionAssert.AreEqual(new[] { "1", "2", "4" }, elegidas.Select(x => x.Candidata.Producto).ToList());

            // sin alternativas, entra la tercera
            List<OfertaElegida> cuatro = ReglaOfertasParaTi.Elegir(e, 4);
            CollectionAssert.AreEqual(new[] { "1", "2", "4", "3" }, cuatro.Select(x => x.Candidata.Producto).ToList());
        }

        [TestMethod]
        public void Elegir_ConProbabilidades_MandaElModelo_LasDemasDetrasEnElOrdenDeLaRegla()
        {
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[]
            {
                Candidata("1", "A", popularidad: 30), Candidata("2", "B", popularidad: 20), Candidata("3", "C", popularidad: 10)
            }, new HistorialClienteOfertas(), HOY);

            List<OfertaElegida> elegidas = ReglaOfertasParaTi.Elegir(e, 3, new Dictionary<string, float> { { "3", 0.9f }, { "2", 0.1f } });

            CollectionAssert.AreEqual(new[] { "3", "2", "1" }, elegidas.Select(x => x.Candidata.Producto).ToList());
            Assert.AreEqual(0.9f, elegidas[0].Probabilidad);
            Assert.IsNull(elegidas[2].Probabilidad);
        }

        [TestMethod]
        public void ConStock_SoloConDisponible()
        {
            List<CandidataEvaluada> e = ReglaOfertasParaTi.Evaluar(new[] { Candidata("1"), Candidata("2"), Candidata("3") }, new HistorialClienteOfertas(), HOY);
            List<CandidataEvaluada> con = ReglaOfertasParaTi.ConStock(e, new Dictionary<string, int> { { "1", 0 }, { "2", 3 } }, 10);
            CollectionAssert.AreEqual(new[] { "2" }, con.Select(x => x.Candidata.Producto).ToList());
        }

        [TestMethod]
        public void Titulo_SinElAnio()
        {
            Assert.AreEqual("Black Friday", ReglaOfertasParaTi.Titulo("Black Friday 2026"));
            Assert.AreEqual("Rebajas de verano", ReglaOfertasParaTi.Titulo("Rebajas de verano 2027"));
            Assert.AreEqual("Feria online", ReglaOfertasParaTi.Titulo("Feria 2026 online"));
            Assert.AreEqual("Pack 45929", ReglaOfertasParaTi.Titulo("Pack 45929"));
            Assert.IsNull(ReglaOfertasParaTi.Titulo(" "));
        }

        [TestMethod]
        public void TextoHasta_DiaDeLaSemanaYMes()
        {
            // El 30/11/2026 es lunes
            Assert.AreEqual("Hasta el lunes 30 de noviembre",ReglaOfertasParaTi.TextoHasta(new DateTime(2026, 11, 30), HOY));
            Assert.AreEqual("Hasta el viernes 8 de enero de 2027", ReglaOfertasParaTi.TextoHasta(new DateTime(2027, 1, 8), HOY));
            Assert.IsNull(ReglaOfertasParaTi.TextoHasta(null, HOY));
        }

        [TestMethod]
        public void FinDeCampana_ElMasTardio_ONullSiAlgunaNoAcaba()
        {
            var a = Candidata("1");
            var b = Candidata("2");
            b.FechaHasta = new DateTime(2026, 12, 2);
            Assert.AreEqual(new DateTime(2026, 12, 2), ReglaOfertasParaTi.FinDeCampana(new[] { a, b }, BF));
            b.FechaHasta = null;
            Assert.IsNull(ReglaOfertasParaTi.FinDeCampana(new[] { a, b }, BF));
        }

        [TestMethod]
        public void FormatoTitulo_NombresDeNesto()
        {
            Assert.AreEqual("Sicurezza BB Cream 50 ml", ReglaOfertasParaTi.FormatoTitulo("SICUREZZA BB CREAM 50 ML"));
            Assert.AreEqual("Jabón de Carbón Activo", ReglaOfertasParaTi.FormatoTitulo("JABÓN DE CARBÓN ACTIVO"));
            Assert.AreEqual("Eva Visnú", ReglaOfertasParaTi.FormatoTitulo("Eva  Visnú"));
        }

        [TestMethod]
        public void CampanaPrincipal_LaDeMasCandidatas()
        {
            var otra = Candidata("9");
            otra.Campana = "Otra";
            Assert.AreEqual(BF, ReglaOfertasParaTi.CampanaPrincipal(new[] { Candidata("1"), Candidata("2"), otra }));
            Assert.IsNull(ReglaOfertasParaTi.CampanaPrincipal(new CandidataOferta[0]));
        }
    }
}
