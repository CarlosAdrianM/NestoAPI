using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Abandono;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Abandono
{
    /// <summary>
    /// NestoAPI#714 (corte 1): la etiqueta como función pura: cadencia (mediana, mínimo 7), horizonte (clamp 60-180), comprador
    /// regular, regla baseline, motivo legible y las features sin fuga (nada posterior al día de la instantánea).
    /// </summary>
    [TestClass]
    public class CalculadoraAbandonoTests
    {
        private static readonly DateTime D = new DateTime(2026, 10, 26); // lunes

        /// <summary>Compras cada <paramref name="cadencia"/> días, la última hace <paramref name="diasSinComprar"/>.</summary>
        internal static DatosClienteAbandono Cliente(int cadencia, int diasSinComprar, int compras, DateTime? hoy = null, decimal importe = 100m)
        {
            DateTime d = hoy ?? D;
            var datos = new DatosClienteAbandono { Empresa = "1", Cliente = "15191", Contacto = "0", Vendedor = "DV", TodosLosGrupos = true, Estado = 0, Grupo = "0", Provincia = "28" };
            DateTime ultima = d.AddDays(-diasSinComprar);
            for (int i = 0; i < compras; i++)
            {
                datos.Compras.Add(new CompraAbandono { Dia = ultima.AddDays(-i * cadencia), Importe = importe });
            }
            return datos;
        }

        [TestMethod]
        public void Cadencia_ConMenosDeDosDias_NoHay()
        {
            Assert.IsNull(CalculadoraAbandono.Cadencia(new DateTime[0]));
            Assert.IsNull(CalculadoraAbandono.Cadencia(new[] { D }));
            Assert.IsNull(CalculadoraAbandono.Cadencia(new[] { D, D })); // el mismo día dos veces es un solo día con compra
        }

        [TestMethod]
        public void Cadencia_EsLaMedianaDeLosHuecos_ImparYPar()
        {
            // Huecos 10, 20, 60 → mediana 20
            Assert.AreEqual(20, CalculadoraAbandono.Cadencia(new[] { D, D.AddDays(-10), D.AddDays(-30), D.AddDays(-90) }));
            // Huecos 10, 30 → mediana 20 (media de los dos centrales)
            Assert.AreEqual(20, CalculadoraAbandono.Cadencia(new[] { D, D.AddDays(-10), D.AddDays(-40) }));
            // Huecos 21, 22 → 21,5 → 22 (mitad hacia arriba)
            Assert.AreEqual(22, CalculadoraAbandono.Cadencia(new[] { D, D.AddDays(-21), D.AddDays(-43) }));
        }

        [TestMethod]
        public void Cadencia_NuncaBajaDeSieteDias()
        {
            Assert.AreEqual(7, CalculadoraAbandono.Cadencia(new[] { D, D.AddDays(-1), D.AddDays(-2), D.AddDays(-3) }));
        }

        [TestMethod]
        public void Cadencia_NoDependeDelOrden()
        {
            Assert.AreEqual(CalculadoraAbandono.Cadencia(new[] { D.AddDays(-90), D, D.AddDays(-30), D.AddDays(-10) }),
                CalculadoraAbandono.Cadencia(new[] { D, D.AddDays(-10), D.AddDays(-30), D.AddDays(-90) }));
        }

        [TestMethod]
        public void Horizonte_EsTresVecesLaCadenciaEntre60Y180()
        {
            Assert.AreEqual(60, CalculadoraAbandono.Horizonte(7));
            Assert.AreEqual(60, CalculadoraAbandono.Horizonte(20));
            Assert.AreEqual(63, CalculadoraAbandono.Horizonte(21));
            Assert.AreEqual(90, CalculadoraAbandono.Horizonte(30));
            Assert.AreEqual(180, CalculadoraAbandono.Horizonte(60));
            Assert.AreEqual(180, CalculadoraAbandono.Horizonte(120));
        }

        [TestMethod]
        public void EsCompradorRegular_CuatroDiasConCompraYDentroDelHorizonte()
        {
            Assert.IsTrue(CalculadoraAbandono.EsCompradorRegular(4, 10, 60));
            Assert.IsTrue(CalculadoraAbandono.EsCompradorRegular(4, 60, 60)); // justo en el horizonte, aún regular
            Assert.IsFalse(CalculadoraAbandono.EsCompradorRegular(3, 10, 60)); // pocas compras
            Assert.IsFalse(CalculadoraAbandono.EsCompradorRegular(10, 61, 60)); // ya se ha ido
        }

        [TestMethod]
        public void Baseline_DiasSinComprarMayorQueUnaVezYMediaLaCadencia()
        {
            Assert.IsTrue(CalculadoraAbandono.Baseline(52, 21)); // 52 > 31,5
            Assert.IsFalse(CalculadoraAbandono.Baseline(31, 21));
            Assert.IsFalse(CalculadoraAbandono.Baseline(30, 20)); // justo 1,5 × no marca
            Assert.IsTrue(CalculadoraAbandono.Baseline(31, 20));
        }

        [TestMethod]
        public void Motivo_EsLegible()
        {
            Assert.AreEqual("Suele pedir cada 21 días y lleva 52 sin pedir", CalculadoraAbandono.Motivo(21, 52));
        }

        [TestMethod]
        public void Evaluar_RegularMarcado_RellenaTodo()
        {
            DatosClienteAbandono datos = Cliente(cadencia: 21, diasSinComprar: 52, compras: 6);

            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(datos, D);

            Assert.IsTrue(e.EsCompradorRegular);
            Assert.AreEqual(6, e.DiasConCompra12Meses);
            Assert.AreEqual(21, e.Cadencia);
            Assert.AreEqual(52, e.DiasSinComprar);
            Assert.AreEqual(63, e.Horizonte);
            Assert.AreEqual(D.AddDays(63), e.FechaLimite);
            Assert.IsTrue(e.Baseline);
            Assert.AreEqual("Suele pedir cada 21 días y lleva 52 sin pedir", e.Motivo);
            Assert.IsNotNull(e.Features);
            Assert.IsFalse(string.IsNullOrEmpty(e.FeaturesJson));
        }

        [TestMethod]
        public void Evaluar_RegularEnPlazo_NoMarca()
        {
            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(Cliente(21, 20, 6), D);
            Assert.IsTrue(e.EsCompradorRegular);
            Assert.IsFalse(e.Baseline);
            Assert.IsNotNull(e.FeaturesJson, "la instantánea semanal guarda también a los que no marca");
        }

        [TestMethod]
        public void Evaluar_PocasCompras_NoEsRegular()
        {
            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(Cliente(21, 10, 3), D);
            Assert.IsFalse(e.EsCompradorRegular);
            Assert.AreEqual(3, e.DiasConCompra12Meses);
            Assert.IsNull(e.FeaturesJson);
        }

        [TestMethod]
        public void Evaluar_MasAlladelHorizonte_YaSeHaIdo_NoEsRegular()
        {
            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(Cliente(21, 64, 6), D);
            Assert.IsFalse(e.EsCompradorRegular);
            Assert.IsTrue(e.Baseline, "la regla sí lo marcaría, pero no es población: ya es un abandono consumado");
        }

        [TestMethod]
        public void Evaluar_SinFuga_IgnoraComprasPosterioresAlDiaYAnterioresA12Meses()
        {
            DatosClienteAbandono datos = Cliente(21, 52, 6);
            datos.Compras.Add(new CompraAbandono { Dia = D.AddDays(1), Importe = 500m }); // mañana: no existe aún
            datos.Compras.Add(new CompraAbandono { Dia = D.AddMonths(-13), Importe = 500m }); // fuera de la ventana

            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(datos, D);

            Assert.AreEqual(6, e.DiasConCompra12Meses);
            Assert.AreEqual(52, e.DiasSinComprar);
            Assert.AreEqual(600m, e.Features.Importe12Meses);
        }

        [TestMethod]
        public void Evaluar_UnPedidoEnCursoCuentaComoDiaConCompraPeroNoComoImporteFacturado()
        {
            DatosClienteAbandono datos = Cliente(21, 52, 6);
            datos.Compras.Add(new CompraAbandono { Dia = D.AddDays(-3), Importe = 80m, EnCurso = true });

            EvaluacionAbandono e = CalculadoraAbandono.Evaluar(datos, D);

            Assert.AreEqual(3, e.DiasSinComprar);
            Assert.IsFalse(e.Baseline);
            Assert.IsTrue(e.Features.PedidoEnCurso);
            Assert.AreEqual(600m, e.Features.Importe12Meses);
            Assert.AreEqual(7, e.DiasConCompra12Meses);
        }

        [TestMethod]
        public void Features_FrecuenciaEImporteDeTresMesesFrenteALosTresAnteriores()
        {
            var datos = new DatosClienteAbandono { Cliente = "1", Contacto = "0", Vendedor = "DV" };
            foreach (int dias in new[] { 10, 40, 70, 100, 130, 160 })
            {
                datos.Compras.Add(new CompraAbandono { Dia = D.AddDays(-dias), Importe = dias });
            }

            FeaturesAbandono f = CalculadoraAbandono.Evaluar(datos, D).Features;

            Assert.AreEqual(3, f.DiasConCompra3Meses); // 10, 40, 70 (3 meses atrás es el 26/07: 92 días)
            Assert.AreEqual(3, f.DiasConCompra3MesesAnteriores); // 100, 130, 160
            Assert.AreEqual(120m, f.Importe3Meses);
            Assert.AreEqual(390m, f.Importe3MesesAnteriores);
            Assert.AreEqual(510m, f.Importe12Meses);
            Assert.AreEqual(85m, f.ImporteMedioPedido12Meses);
            Assert.AreEqual(10m, f.ImporteUltimaCompraFacturada);
            Assert.AreEqual(30, f.Cadencia);
            Assert.AreEqual(0, f.RangoIntercuartilCadencia);
            Assert.AreEqual(30, f.MaxDiasEntreCompras);
            Assert.AreEqual(10, f.Mes);
            Assert.AreEqual(D, f.Instantanea);
            Assert.AreEqual(FeaturesAbandono.VERSION, f.Version);
        }

        [TestMethod]
        public void Features_RapportsGruposImpagosYVendedor()
        {
            DatosClienteAbandono datos = Cliente(21, 52, 6);
            datos.Contactos12Meses = 10;
            datos.NoContactados12Meses = 4;
            datos.Contactos3Meses = 2;
            datos.NoContactados3Meses = 3;
            datos.RapportsConPedido12Meses = 4;
            datos.UltimoContacto = D.AddDays(-15);
            datos.UltimoIntento = D.AddDays(-2);
            datos.UltimoCambioVendedor = D.AddDays(-40);
            datos.Impagos12Meses = 1;
            datos.ImporteVencido = 120.5m;
            datos.ImportePendiente = 300m;
            datos.Devoluciones12Meses = 2;
            datos.ImporteDevoluciones12Meses = 35m;
            datos.Pedidos24Meses = 14;
            datos.PrimeraCompra24Meses = D.AddMonths(-20).AddDays(3);
            datos.Empleados = 3;
            datos.PorGrupo.Add(new CompraGrupoAbandono { Grupo = "PEL", UltimaCompra = D.AddDays(-52), Pedidos12Meses = 5 });
            datos.PorGrupo.Add(new CompraGrupoAbandono { Grupo = "COS", UltimaCompra = D.AddDays(-140), Pedidos12Meses = 1 });

            FeaturesAbandono f = CalculadoraAbandono.Evaluar(datos, D).Features;

            Assert.AreEqual(0.4, f.ConversionRapports12Meses);
            Assert.AreEqual(15, f.DiasDesdeUltimoContacto);
            Assert.AreEqual(2, f.DiasDesdeUltimoIntento);
            Assert.AreEqual(40, f.DiasDesdeCambioVendedor);
            Assert.AreEqual(1, f.Impagos12Meses);
            Assert.AreEqual(120.5m, f.ImporteVencido);
            Assert.AreEqual(300m, f.ImportePendiente);
            Assert.AreEqual(2, f.Devoluciones12Meses);
            Assert.AreEqual(35m, f.ImporteDevoluciones12Meses);
            Assert.AreEqual(14, f.Pedidos24Meses);
            Assert.AreEqual(19, f.MesesDesdePrimeraCompra24Meses);
            Assert.AreEqual(3, f.Empleados);
            Assert.AreEqual(2, f.GruposDistintos12Meses);
            Assert.AreEqual(52, f.DiasSinComprarPorGrupo["PEL"]);
            Assert.AreEqual(140, f.DiasSinComprarPorGrupo["COS"]);
            Assert.AreEqual(5, f.PedidosPorGrupo12Meses["PEL"]);
            Assert.AreEqual(2.476, f.RatioRecencia);
        }

        [TestMethod]
        public void Features_SinRapportsNiCambioDeVendedor_Nulos()
        {
            FeaturesAbandono f = CalculadoraAbandono.Evaluar(Cliente(21, 52, 6), D).Features;
            Assert.IsNull(f.ConversionRapports12Meses);
            Assert.IsNull(f.DiasDesdeUltimoContacto);
            Assert.IsNull(f.DiasDesdeUltimoIntento);
            Assert.IsNull(f.DiasDesdeCambioVendedor);
            Assert.IsNull(f.MesesDesdePrimeraCompra24Meses);
        }

        [TestMethod]
        public void Serializar_FechasCortasYVersionDentro()
        {
            string json = CalculadoraAbandono.Evaluar(Cliente(21, 52, 6), D).FeaturesJson;
            JObject o = JObject.Parse(json);
            Assert.AreEqual("features-1", (string)o["Version"]);
            Assert.AreEqual("2026-10-26", (string)o["Instantanea"]);
            Assert.AreEqual(21, (int)o["Cadencia"]);
            Assert.AreEqual(52, (int)o["DiasSinComprar"]);
            Assert.AreEqual(63, (int)o["Horizonte"]);
            Assert.IsNull(((JValue)o["ConversionRapports12Meses"]).Value);
        }

        [TestMethod]
        public void RangoIntercuartil_Q3MenosQ1()
        {
            Assert.AreEqual(0, CalculadoraAbandono.RangoIntercuartil(new List<int> { 10, 20, 30 }));
            Assert.AreEqual(20, CalculadoraAbandono.RangoIntercuartil(new List<int> { 10, 20, 30, 40 })); // Q1 15, Q3 35
            Assert.AreEqual(28, CalculadoraAbandono.RangoIntercuartil(new List<int> { 7, 14, 21, 28, 35, 42, 49, 56 })); // Q1 17,5, Q3 45,5 → 28
        }

        [TestMethod]
        public void Huecos_DistintosYOrdenados()
        {
            // Ordenados: D-30, D-10, D → huecos 20 y 10 (el día repetido cuenta una vez)
            CollectionAssert.AreEqual(new List<int> { 20, 10 }, CalculadoraAbandono.Huecos(new[] { D.AddDays(-30), D, D.AddDays(-10), D.AddDays(-10) }));
        }
    }
}
