using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Remesas
{
    /// <summary>NestoAPI#896: modelo de probabilidad de devolución de los recibos de una remesa, en sombra.</summary>
    [TestClass]
    public class ModeloImpagosRemesasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 22);
        private string carpeta;
        private int orden;

        [TestInitialize]
        public void Preparar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "impagos896_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(carpeta);
            orden = 1;
            ModeloImpagosRemesasJobsService.OlvidarAvisos();
            SombraImpagosRemesa.OlvidarAvisos();
            SombraImpagosRemesa.Encolador = null;
            PredictorImpagos.Olvidar();
        }

        [TestCleanup]
        public void Limpiar()
        {
            SombraImpagosRemesa.Encolador = null;
            PredictorImpagos.Olvidar();
            try
            {
                Directory.Delete(carpeta, true);
            }
            catch (Exception)
            {
            }
        }

        private ReciboGirado Recibo(DateTime giro, string cliente = "100", string documento = null, string efecto = "1", double importe = 100,
            DateTime? impago = null, string motivo = "Saldo insuficiente.", string ccc = "1")
        {
            int n = orden++;
            return new ReciboGirado
            {
                Empresa = "1",
                NumOrden = n,
                Cliente = cliente,
                Contacto = "0",
                FechaFactura = giro.AddDays(-30),
                FechaVto = giro,
                FechaGiro = giro,
                Documento = documento ?? "NV" + n,
                Efecto = efecto,
                Importe = importe,
                Remesa = 1,
                Vendedor = "NV",
                FormaVenta = "DIR",
                Ccc = ccc,
                Entidad = "2100",
                FechaImpago = impago,
                MotivoImpago = impago.HasValue ? motivo : null
            };
        }

        private static string Json(RasgosImpago r) => JsonConvert.SerializeObject(r);

        #region Fecha del giro y enlace

        [TestMethod]
        public void FechaGiro_MandaLaRemesa_SiNoElVencimiento_YNuncaAntesDeLaFactura()
        {
            var factura = new DateTime(2026, 5, 1);
            Assert.AreEqual(new DateTime(2026, 5, 29), ReglaFechaGiro.Calcular(factura, new DateTime(2026, 5, 31), new DateTime(2026, 5, 29, 9, 30, 0)));
            Assert.AreEqual(new DateTime(2026, 5, 31), ReglaFechaGiro.Calcular(factura, new DateTime(2026, 5, 31), null));
            Assert.AreEqual(factura, ReglaFechaGiro.Calcular(factura, null, null));
            Assert.AreEqual(factura, ReglaFechaGiro.Calcular(factura, new DateTime(2026, 4, 1), null), "un vencimiento anterior a la factura no vale");
            Assert.AreEqual(new DateTime(2026, 5, 31), ReglaFechaGiro.Calcular(factura, new DateTime(2026, 5, 31), new DateTime(2020, 1, 1)), "una remesa anterior a la factura no vale");
        }

        [TestMethod]
        public void Enlazar_ElImpagadoVaAlUltimoGiroAnteriorDelMismoEfecto()
        {
            ReciboGirado primero = Recibo(new DateTime(2026, 3, 1), documento: "NV1");
            ReciboGirado regiro = Recibo(new DateTime(2026, 4, 1), documento: "NV1");
            ReciboGirado otro = Recibo(new DateTime(2026, 3, 1), documento: "NV2");
            var impagados = new List<ImpagadoLeido>
            {
                new ImpagadoLeido { Empresa = "1", Cliente = "100", Documento = "NV1", Efecto = "1", Fecha = new DateTime(2026, 4, 8), Concepto = "Saldo insuficiente." },
                new ImpagadoLeido { Empresa = "1", Cliente = "100", Documento = "NV1", Efecto = "1", Fecha = new DateTime(2026, 3, 9), Concepto = "Mandato no válido o inexistente." },
                new ImpagadoLeido { Empresa = "1", Cliente = "100", Documento = "NV9", Efecto = "1", Fecha = new DateTime(2026, 3, 9), Concepto = "Sin recibo" }
            };

            int enlazados = EnlazadorImpagados.Enlazar(new[] { primero, regiro, otro }, impagados);

            Assert.AreEqual(2, enlazados);
            Assert.AreEqual(new DateTime(2026, 3, 9), primero.FechaImpago);
            Assert.AreEqual("Mandato no válido o inexistente.", primero.MotivoImpago);
            Assert.AreEqual(new DateTime(2026, 4, 8), regiro.FechaImpago);
            Assert.IsNull(otro.FechaImpago);
        }

        [TestMethod]
        public void Enlazar_UnImpagadoAnteriorAlGiroNoSeEnlaza()
        {
            ReciboGirado recibo = Recibo(new DateTime(2026, 4, 1), documento: "NV1");
            var impagados = new List<ImpagadoLeido> { new ImpagadoLeido { Empresa = "1", Cliente = "100", Documento = "NV1", Efecto = "1", Fecha = new DateTime(2026, 3, 20) } };

            Assert.AreEqual(0, EnlazadorImpagados.Enlazar(new[] { recibo }, impagados));
            Assert.IsNull(recibo.FechaImpago);
        }

        [TestMethod]
        public void Etiqueta_DevueltoSoloSiVuelveEnLos45DiasSiguientes()
        {
            Assert.IsTrue(Recibo(new DateTime(2026, 1, 1), impago: new DateTime(2026, 2, 15)).Devuelto, "45 días justos");
            Assert.IsFalse(Recibo(new DateTime(2026, 1, 1), impago: new DateTime(2026, 2, 16)).Devuelto, "46 días");
            Assert.IsFalse(Recibo(new DateTime(2026, 1, 1)).Devuelto);
        }

        #endregion

        #region Rasgos sin fuga

        [TestMethod]
        public void Rasgos_SinFuga_UnImpagadoPosteriorAlGiroNoInfluye()
        {
            var dia = new DateTime(2026, 6, 10);
            ReciboGirado anterior = Recibo(new DateTime(2026, 5, 20));
            ReciboGirado mio = Recibo(dia);
            string antes = Json(ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio }, null));

            // El recibo anterior vuelve DESPUÉS de mi giro (y el mismo día): ese día no se sabía.
            anterior.FechaImpago = dia.AddDays(3);
            anterior.MotivoImpago = "Saldo insuficiente.";
            Assert.AreEqual(antes, Json(ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio }, null)));
            anterior.FechaImpago = dia;
            Assert.AreEqual(antes, Json(ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio }, null)), "el impagado del mismo día tampoco se conocía");

            // Si volvió ANTES de mi giro, sí cuenta.
            anterior.FechaImpago = dia.AddDays(-1);
            RasgosImpago conocido = ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio }, null);
            Assert.AreNotEqual(antes, Json(conocido));
            Assert.AreEqual(1f, conocido.DiasDesdeUltimaDevolucion);
            Assert.AreEqual(1f, conocido.DevolucionesTotales);
            Assert.AreEqual(MotivosImpago.SALDO, conocido.MotivoUltima);
        }

        [TestMethod]
        public void Rasgos_SinFuga_NiSuPropioImpagadoNiLosRecibosPosterioresInfluyen()
        {
            var dia = new DateTime(2026, 6, 10);
            ReciboGirado anterior = Recibo(new DateTime(2026, 5, 20));
            ReciboGirado mio = Recibo(dia);
            string antes = Json(ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio }, null));

            mio.FechaImpago = dia.AddDays(5);
            mio.MotivoImpago = "Saldo insuficiente.";
            ReciboGirado posterior = Recibo(dia.AddDays(10), impago: dia.AddDays(15));
            ReciboGirado delMismoDia = Recibo(dia);

            Assert.AreEqual(antes, Json(ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { anterior, mio, posterior, delMismoDia }, null)));
        }

        [TestMethod]
        public void Rasgos_SinFuga_ElNivelGeneralSoloMiraDiasAnteriores()
        {
            var dia = new DateTime(2026, 6, 10);
            var recibos = new Dictionary<DateTime, int> { [dia.AddDays(-20)] = 150, [dia.AddDays(-9)] = 50, [dia.AddDays(-3)] = 9999, [dia] = 9999 };
            var impagados = new Dictionary<DateTime, int> { [dia.AddDays(-5)] = 6, [dia.AddDays(-14)] = 4, [dia.AddDays(-15)] = 50, [dia] = 1000, [dia.AddDays(2)] = 1000 };

            var nivel = new NivelGeneralImpagos(recibos, impagados);

            Assert.AreEqual(10f / 200f, nivel.Nivel14(dia), 1e-6f, "devoluciones de los 14 días anteriores / recibos de 24 a 8 días antes");
            Assert.AreEqual(60f / 200f, nivel.Nivel30(dia), 1e-6f);
        }

        [TestMethod]
        public void NivelGeneral_SinRecibosSuficientes_MenosUno()
        {
            var dia = new DateTime(2026, 6, 10);
            var nivel = new NivelGeneralImpagos(new Dictionary<DateTime, int> { [dia.AddDays(-20)] = 99 }, new Dictionary<DateTime, int> { [dia.AddDays(-5)] = 6 });

            Assert.AreEqual(-1f, nivel.Nivel14(dia));
            Assert.AreEqual(-1f, nivel.Nivel30(dia));
        }

        [TestMethod]
        public void Rasgos_HistoriaDelCliente()
        {
            var dia = new DateTime(2026, 6, 10);
            var historia = new List<ReciboGirado>
            {
                Recibo(new DateTime(2025, 1, 10), importe: 100, impago: new DateTime(2025, 1, 18), motivo: "Mandato no válido o inexistente."),
                Recibo(new DateTime(2026, 2, 10), importe: 200),
                Recibo(new DateTime(2026, 4, 10), documento: "NV77", importe: 300, impago: new DateTime(2026, 4, 20)),
                Recibo(new DateTime(2026, 5, 20), importe: 400)
            };
            ReciboGirado mio = Recibo(dia, documento: "NV77", importe: 600, ccc: "2");
            mio.FechaFactura = new DateTime(2026, 3, 10);

            RasgosImpago r = ConstructorRasgosImpagos.Rasgos(mio, dia, historia.Concat(new[] { mio }), null);

            Assert.AreEqual(51f, r.DiasDesdeUltimaDevolucion);
            Assert.AreEqual(MotivosImpago.SALDO, r.MotivoUltima);
            Assert.AreEqual(1f, r.Devoluciones3Meses);
            Assert.AreEqual(1f, r.Devoluciones12Meses);
            Assert.AreEqual(2f, r.DevolucionesTotales);
            Assert.AreEqual(4f, r.RecibosAnteriores);
            Assert.AreEqual(3f, r.Recibos12Meses);
            Assert.AreEqual(0.5f, r.TasaDevolucionCliente);
            Assert.AreEqual((float)(dia - new DateTime(2025, 1, 10)).Days, r.AntiguedadDias);
            Assert.AreEqual(1f, r.RecibosUltimos45, "el del 20/05 sigue pudiendo volver");
            Assert.AreEqual(600f, r.Importe);
            Assert.AreEqual(3f, r.RelacionImporteHabitual, "600 / mediana (200) de sus recibos anteriores");
            Assert.AreEqual(1f, r.EsRegiro, "ese documento y efecto ya se devolvió");
            Assert.AreEqual(1f, r.PrimerGiroCuenta, "nunca se le giró por la ficha 2");
            Assert.AreEqual(92f, r.DiasFacturaAGiro);
            Assert.AreEqual(10f, r.DiaMes);
            Assert.AreEqual(6f, r.Mes);
            Assert.AreEqual(-1f, r.NivelGeneral14);
        }

        [TestMethod]
        public void Rasgos_PrimerRecibo()
        {
            var dia = new DateTime(2026, 6, 10);
            ReciboGirado mio = Recibo(dia);

            RasgosImpago r = ConstructorRasgosImpagos.Rasgos(mio, dia, new[] { mio }, null);

            Assert.AreEqual(-1f, r.DiasDesdeUltimaDevolucion);
            Assert.AreEqual(MotivosImpago.NINGUNA, r.MotivoUltima);
            Assert.AreEqual(0f, r.RecibosAnteriores);
            Assert.AreEqual(-1f, r.TasaDevolucionCliente);
            Assert.AreEqual(-1f, r.AntiguedadDias);
            Assert.AreEqual(-1f, r.RelacionImporteHabitual);
            Assert.AreEqual(0f, r.EsRegiro);
        }

        [TestMethod]
        public void Construir_LosGirosDeLosUltimos45DiasNoEntrenan_PeroSiCuentanComoHistoria()
        {
            ReciboGirado viejo = Recibo(HOY.AddDays(-200), impago: HOY.AddDays(-150));
            ReciboGirado enElCorte = Recibo(HOY.AddDays(-45));
            ReciboGirado reciente = Recibo(HOY.AddDays(-44));
            ReciboGirado deOtro = Recibo(HOY.AddDays(-100), cliente: "200");

            List<EjemploImpago> ejemplos = ConstructorRasgosImpagos.Construir(new[] { reciente, enElCorte, viejo, deOtro }, null, HOY);

            CollectionAssert.AreEqual(new[] { viejo.NumOrden, deOtro.NumOrden, enElCorte.NumOrden }, ejemplos.Select(e => e.Recibo.NumOrden).ToArray());
            Assert.IsFalse(ejemplos[0].Devuelto, "volvió a los 50 días: fuera de la etiqueta");
            Assert.AreEqual(1f, ejemplos[2].Rasgos.DevolucionesTotales, "pero para el siguiente giro es una devolución conocida");
            Assert.AreEqual(0f, ejemplos[1].Rasgos.RecibosAnteriores, "la historia es por cliente");
        }

        [TestMethod]
        public void Construir_Desde_DejaFueraLosPrimerosSinHistoria()
        {
            ReciboGirado primero = Recibo(HOY.AddDays(-400));
            ReciboGirado segundo = Recibo(HOY.AddDays(-100));

            List<EjemploImpago> ejemplos = ConstructorRasgosImpagos.Construir(new[] { primero, segundo }, null, HOY, HOY.AddDays(-300));

            Assert.AreEqual(1, ejemplos.Count);
            Assert.AreEqual(1f, ejemplos[0].Rasgos.RecibosAnteriores);
        }

        [TestMethod]
        public void MotivosImpago_Clasificar()
        {
            Assert.AreEqual(MotivosImpago.SALDO, MotivosImpago.Clasificar("Saldo insuficiente."));
            Assert.AreEqual(MotivosImpago.MANDATO, MotivosImpago.Clasificar("Mandato no válido o inexistente"));
            Assert.AreEqual(MotivosImpago.MANDATO, MotivosImpago.Clasificar("Faltan datos del mandato"));
            Assert.AreEqual(MotivosImpago.CUENTA, MotivosImpago.Clasificar("Cuenta bloqueada y/o cuenta cancelada"));
            Assert.AreEqual(MotivosImpago.CUENTA, MotivosImpago.Clasificar("Número de cuenta incorrecto"));
            Assert.AreEqual(MotivosImpago.DISCONFORME, MotivosImpago.Clasificar("Transacción autorizada disconforme"));
            Assert.AreEqual(MotivosImpago.OTRO, MotivosImpago.Clasificar("Razón no especificada por el cliente"));
            Assert.AreEqual(MotivosImpago.OTRO, MotivosImpago.Clasificar(null));
        }

        [TestMethod]
        public void MapaCategorias_LasPocoFrecuentesVanComoOtros()
        {
            var ejemplos = new List<EjemploImpago>();
            for (int i = 0; i < 5; i++)
            {
                ejemplos.Add(new EjemploImpago { Recibo = Recibo(HOY), Rasgos = new RasgosImpago { Vendedor = i < 3 ? "NV" : "RARO" + i, Entidad = "2100", FormaVenta = "DIR", MotivoUltima = MotivosImpago.NINGUNA } });
            }

            MapaCategoriasImpagos mapa = MapaCategoriasImpagos.Desde(ejemplos, minimo: 3);

            CollectionAssert.AreEqual(new[] { "NV" }, mapa.Vendedores);
            Assert.AreEqual("NV", mapa.Aplicar(new RasgosImpago { Vendedor = "nv", Entidad = "2100", FormaVenta = "DIR" }).Vendedor);
            Assert.AreEqual(MapaCategoriasImpagos.OTROS, mapa.Aplicar(new RasgosImpago { Vendedor = "RARO3", Entidad = "9999", FormaVenta = "DIR" }).Vendedor);
            Assert.AreEqual(MapaCategoriasImpagos.OTROS, mapa.Aplicar(new RasgosImpago { Vendedor = "RARO3", Entidad = "9999", FormaVenta = "DIR" }).Entidad);
            FilaImpago fila = mapa.Fila(new EjemploImpago { Recibo = Recibo(HOY), Devuelto = true, Rasgos = new RasgosImpago { Vendedor = "NV", Importe = 12 } });
            Assert.IsTrue(fila.Label);
            Assert.AreEqual(12f, fila.Importe);
            Assert.AreEqual(MotivosImpago.NINGUNA, fila.MotivoUltima);
        }

        #endregion

        #region LA REGLA

        [TestMethod]
        public void Regla_Tramo_ComoElBisectLeftDelAnalisis()
        {
            Assert.AreEqual(0, ReglaImpagos.Tramo(30, ReglaImpagos.CortesUltimaDevolucion), "hasta 30 días");
            Assert.AreEqual(1, ReglaImpagos.Tramo(31, ReglaImpagos.CortesUltimaDevolucion));
            Assert.AreEqual(4, ReglaImpagos.Tramo(366, ReglaImpagos.CortesUltimaDevolucion));
            Assert.AreEqual(0, ReglaImpagos.Tramo(0, ReglaImpagos.CortesDevoluciones12Meses));
            Assert.AreEqual(3, ReglaImpagos.Tramo(5, ReglaImpagos.CortesDevoluciones12Meses), "3 o más");
            Assert.AreEqual(0, ReglaImpagos.Tramo(0, ReglaImpagos.CortesRecibosAnteriores), "es su primer recibo");
            Assert.AreEqual(6, ReglaImpagos.Tramo(51, ReglaImpagos.CortesRecibosAnteriores), "más de 50");
            Assert.AreEqual(3, ReglaImpagos.Tramo(400, ReglaImpagos.CortesImporte), "200-400 €");
        }

        [TestMethod]
        public void Regla_Tramos_NuncaHaDevueltoYSinNivelGeneral_SonElTramoSinDato()
        {
            Dictionary<string, int> tramos = ReglaImpagos.Tramos(new RasgosImpago { DiasDesdeUltimaDevolucion = -1, NivelGeneral14 = -1, DiaMes = 28, Importe = 60, RecibosUltimos45 = 2 });

            Assert.AreEqual(ReglaImpagos.SIN_DATO, tramos[ReglaImpagos.ULTIMA_DEVOLUCION]);
            Assert.AreEqual(ReglaImpagos.SIN_DATO, tramos[ReglaImpagos.NIVEL_GENERAL]);
            Assert.AreEqual(5, tramos[ReglaImpagos.DIA_MES]);
            Assert.AreEqual(1, tramos[ReglaImpagos.IMPORTE]);
            Assert.AreEqual(2, tramos[ReglaImpagos.RECIBOS_ULTIMOS_45]);
            Assert.AreEqual(7, tramos.Count);
        }

        private EjemploImpago Ejemplo(bool devuelto, float diasUltima, DateTime? giro = null)
            => new EjemploImpago
            {
                Recibo = Recibo(giro ?? HOY.AddDays(-100)),
                Devuelto = devuelto,
                Rasgos = new RasgosImpago { DiasDesdeUltimaDevolucion = diasUltima, NivelGeneral14 = -1, DiaMes = 12, Importe = 100 }
            };

        [TestMethod]
        public void Regla_Ajustar_PesoDelTramoEsElLogOddsSuavizadoMenosElDeLaBase()
        {
            // 100 que nunca han devuelto (2 vuelven) y 20 con devolución reciente (8 vuelven): base 10/120.
            var ejemplos = new List<EjemploImpago>();
            for (int i = 0; i < 100; i++) { ejemplos.Add(Ejemplo(i < 2, -1)); }
            for (int i = 0; i < 20; i++) { ejemplos.Add(Ejemplo(i < 8, 10)); }

            PesosReglaImpagos pesos = ReglaImpagos.Ajustar(ejemplos, HOY);

            double tasaBase = 10.0 / 120;
            double lb = Math.Log(tasaBase / (1 - tasaBase));
            double pReciente = (8 + 20 * tasaBase) / (20 + 20);
            double pNunca = (2 + 20 * tasaBase) / (100 + 20);
            Assert.AreEqual(120, pesos.Ejemplos);
            Assert.AreEqual(lb, pesos.LogOddsBase, 1e-9);
            Assert.AreEqual(Math.Log(pReciente / (1 - pReciente)) - lb, pesos.Pesos[ReglaImpagos.Clave(ReglaImpagos.ULTIMA_DEVOLUCION, 0)], 1e-5);
            Assert.AreEqual(Math.Log(pNunca / (1 - pNunca)) - lb, pesos.Pesos[ReglaImpagos.Clave(ReglaImpagos.ULTIMA_DEVOLUCION, ReglaImpagos.SIN_DATO)], 1e-5);
            Assert.AreEqual(0, pesos.Pesos[ReglaImpagos.Clave(ReglaImpagos.DIA_MES, 2)], 1e-9, "un tramo en el que están todos pesa cero");

            double reciente = ReglaImpagos.Probabilidad(pesos, Ejemplo(false, 10).Rasgos);
            double nunca = ReglaImpagos.Probabilidad(pesos, Ejemplo(false, -1).Rasgos);
            double esperado = 1 / (1 + Math.Exp(-(lb + ReglaImpagos.ENCOGE * (Math.Log(pReciente / (1 - pReciente)) - lb))));
            Assert.AreEqual(esperado, reciente, 1e-5);
            Assert.IsTrue(reciente > 0.15 && nunca < 0.06, $"reciente {reciente}, nunca {nunca}");
        }

        [TestMethod]
        public void Regla_UnTramoQueNoSeVioAlAjustar_NiSumaNiResta()
        {
            var ejemplos = new List<EjemploImpago>();
            for (int i = 0; i < 50; i++) { ejemplos.Add(Ejemplo(i < 5, -1)); }
            PesosReglaImpagos pesos = ReglaImpagos.Ajustar(ejemplos, HOY);

            double p = ReglaImpagos.Probabilidad(pesos, new RasgosImpago { DiasDesdeUltimaDevolucion = 200, NivelGeneral14 = 0.05f, DiaMes = 30, Importe = 5000, RecibosAnteriores = 80, RecibosUltimos45 = 9, Devoluciones12Meses = 7 });

            Assert.AreEqual(0.1, p, 1e-9, "la tasa base");
        }

        [TestMethod]
        public void Regla_Ajustar_SinDevolucionesOSinEjemplos_Null()
        {
            Assert.IsNull(ReglaImpagos.Ajustar(null, HOY));
            Assert.IsNull(ReglaImpagos.Ajustar(Enumerable.Range(0, 10).Select(_ => Ejemplo(false, -1)), HOY));
            Assert.IsNull(ReglaImpagos.Ajustar(Enumerable.Range(0, 10).Select(_ => Ejemplo(true, -1)), HOY));
        }

        [TestMethod]
        public void TextosRiesgo_LasDosCausasQueMasSuben_ConLosDatosDelRecibo()
        {
            var rasgos = new RasgosImpago { DiasDesdeUltimaDevolucion = 12, MotivoUltima = MotivosImpago.SALDO, Devoluciones12Meses = 3, RecibosAnteriores = 40, Importe = 250.5f, DiaMes = 28, NivelGeneral14 = -1 };
            var contribuciones = new Dictionary<string, double>
            {
                [ReglaImpagos.ULTIMA_DEVOLUCION] = 1.8,
                [ReglaImpagos.DEVOLUCIONES_12_MESES] = 1.2,
                [ReglaImpagos.DIA_MES] = 0.3,
                [ReglaImpagos.RECIBOS_ANTERIORES] = -0.5
            };

            List<string> motivos = TextosRiesgoImpago.Motivos(contribuciones, rasgos);

            CollectionAssert.AreEqual(new[] { "devolvió un recibo hace 12 días (saldo insuficiente)", "ha devuelto 3 veces en el último año" }, motivos);
        }

        [TestMethod]
        public void TextosRiesgo_SinCausasPositivasOQueNoSeSabenContar_Vacio()
        {
            var rasgos = new RasgosImpago { DiasDesdeUltimaDevolucion = -1, RecibosAnteriores = 40, DiaMes = 12, NivelGeneral14 = -1 };

            Assert.AreEqual(0, TextosRiesgoImpago.Motivos(new Dictionary<string, float> { ["Mes"] = 2f, ["DiaMes"] = 1f, ["DiasDesdeUltimaDevolucion"] = 0.5f, ["Importe"] = -1f }, rasgos).Count);
            Assert.AreEqual("es su primer recibo", TextosRiesgoImpago.Frase(nameof(RasgosImpago.RecibosAnteriores), new RasgosImpago()));
            Assert.AreEqual("ha devuelto 1 vez en los últimos tres meses", TextosRiesgoImpago.Frase(nameof(RasgosImpago.Devoluciones3Meses), new RasgosImpago { Devoluciones3Meses = 1 }));
        }

        #endregion

        #region Métricas, calibración, puerta y fiabilidad

        [TestMethod]
        public void Metricas_Auc_PerfectoInvertidoYEmpates()
        {
            var perfecto = new List<(double, bool)> { (0.9, true), (0.8, true), (0.2, false), (0.1, false) };
            var invertido = perfecto.Select(p => (1 - p.Item1, p.Item2)).ToList();
            var empate = new List<(double, bool)> { (0.5, true), (0.5, false), (0.5, true), (0.5, false) };

            Assert.AreEqual(1.0, CalculoMetricasImpagos.Auc(perfecto));
            Assert.AreEqual(0.0, CalculoMetricasImpagos.Auc(invertido));
            Assert.AreEqual(0.5, CalculoMetricasImpagos.Auc(empate));
            Assert.IsNull(CalculoMetricasImpagos.Auc(new List<(double, bool)> { (0.5, false) }));
        }

        [TestMethod]
        public void Metricas_CaptadoYTramosYCalibracionDeLosAltos()
        {
            // 100 recibos: 5 al 40 % (vuelven 2), 5 al 20 % (vuelve 1), 10 al 10 % (vuelve 1) y 80 al 1 % (vuelve 1).
            var pares = new List<(double, bool)>();
            for (int i = 0; i < 5; i++) { pares.Add((0.40, i < 2)); }
            for (int i = 0; i < 5; i++) { pares.Add((0.20, i < 1)); }
            for (int i = 0; i < 10; i++) { pares.Add((0.10, i < 1)); }
            for (int i = 0; i < 80; i++) { pares.Add((0.01, i < 1)); }

            MetricasImpagos m = CalculoMetricasImpagos.Calcular(pares);

            Assert.AreEqual(100, m.Recibos);
            Assert.AreEqual(5, m.Devueltos);
            Assert.AreEqual(40.0, m.Captado5, "el 5 % más arriesgado (5 recibos) recoge 2 de las 5");
            Assert.AreEqual(60.0, m.Captado10);
            Assert.AreEqual(10, m.RecibosAltos);
            Assert.AreEqual(1.0, m.CalibracionAltos, "previstos 5 × 0,4 + 5 × 0,2 = 3; reales 3");
            CollectionAssert.AreEqual(new[] { "< 5 %", "5-15 %", "15-30 %", "≥ 30 %" }, m.Tramos.Select(t => t.Tramo).ToArray());
            CollectionAssert.AreEqual(new[] { 80, 10, 5, 5 }, m.Tramos.Select(t => t.Recibos).ToArray());
            Assert.AreEqual(40.0, m.Tramos[3].PrevistoPorcentaje);
            Assert.AreEqual(40.0, m.Tramos[3].RealPorcentaje);
            Assert.AreEqual(1.25, m.Tramos[0].RealPorcentaje);
            Assert.IsTrue(m.Auc > 0.8);
        }

        [TestMethod]
        public void Metricas_SinRecibos_TramosVaciosYSinAuc()
        {
            MetricasImpagos m = CalculoMetricasImpagos.Calcular(null);

            Assert.AreEqual(0, m.Recibos);
            Assert.IsNull(m.Auc);
            Assert.IsNull(m.CalibracionAltos);
            Assert.AreEqual(4, m.Tramos.Count);
            Assert.IsNull(m.Tramos[0].PrevistoPorcentaje);
        }

        [TestMethod]
        public void Platt_RecuperaLaProbabilidadDeUnaPuntuacionMalEscalada()
        {
            // Puntuaciones s con probabilidad real sigmoide(2s − 3).
            var azar = new Random(896);
            var pares = new List<(double, bool)>();
            for (int i = 0; i < 20000; i++)
            {
                double s = azar.NextDouble() * 4 - 1;
                double p = 1 / (1 + Math.Exp(-(2 * s - 3)));
                pares.Add((s, azar.NextDouble() < p));
            }

            CalibradorPlatt platt = CalibradorPlatt.Ajustar(pares);

            Assert.AreEqual(2.0, platt.A, 0.15);
            Assert.AreEqual(-3.0, platt.B, 0.2);
            Assert.AreEqual(1 / (1 + Math.Exp(1)), platt.Probabilidad(1), 0.03);
        }

        [TestMethod]
        public void Platt_SinUnaDeLasClases_Identidad()
        {
            CalibradorPlatt platt = CalibradorPlatt.Ajustar(new List<(double, bool)> { (1, false), (2, false) });

            Assert.AreEqual(1.0, platt.A);
            Assert.AreEqual(0.0, platt.B);
            Assert.AreEqual(0.5, platt.Probabilidad(0));
        }

        private static MetricasImpagos M(double auc, double captado10 = 40, int devueltos = 100)
            => new MetricasImpagos { Recibos = 3000, Devueltos = devueltos, Auc = auc, Captado5 = 30, Captado10 = captado10, CalibracionAltos = 1 };

        [TestMethod]
        public void Puerta_PocasDevoluciones_NoPromueve()
        {
            DecisionPuertaImpagos d = PuertaCalidadImpagos.Decidir(null, false, M(0.9, devueltos: 29), M(0.8));

            Assert.IsFalse(d.Promover);
            StringAssert.Contains(d.Motivo, "pocas devoluciones");
        }

        [TestMethod]
        public void Puerta_PeorQueLaRegla_NoPromueveAunqueNoHayaModelo()
        {
            DecisionPuertaImpagos d = PuertaCalidadImpagos.Decidir(null, false, M(0.80), M(0.85));

            Assert.IsFalse(d.Promover);
            StringAssert.Contains(d.Motivo, "peor que la regla");
        }

        [TestMethod]
        public void Puerta_SinModeloGuardadoYNoPeorQueLaRegla_Promueve()
        {
            Assert.IsTrue(PuertaCalidadImpagos.Decidir(null, false, M(0.86), M(0.85)).Promover);
            Assert.IsTrue(PuertaCalidadImpagos.Decidir(null, false, M(0.845), M(0.85)).Promover, "dentro de la tolerancia");
            Assert.IsTrue(PuertaCalidadImpagos.Decidir(M(0.99), false, M(0.86), M(0.85)).Promover, "sin zip no hay con qué comparar");
        }

        [TestMethod]
        public void Puerta_ConModeloGuardado_SoloSiNoEmpeora()
        {
            Assert.IsTrue(PuertaCalidadImpagos.Decidir(M(0.87), true, M(0.865), M(0.85)).Promover);
            DecisionPuertaImpagos peorAuc = PuertaCalidadImpagos.Decidir(M(0.90), true, M(0.87), M(0.85));
            Assert.IsFalse(peorAuc.Promover);
            StringAssert.Contains(peorAuc.Motivo, "ordena peor");
            DecisionPuertaImpagos peorCaptado = PuertaCalidadImpagos.Decidir(M(0.87, 45), true, M(0.87, 41), M(0.85));
            Assert.IsFalse(peorCaptado.Promover);
            StringAssert.Contains(peorCaptado.Motivo, "10 %");
        }

        /// <summary>Predicciones resueltas de una semana: bien ordenadas y bien calibradas (o no).</summary>
        private static IEnumerable<PrediccionResueltaImpago> Semana(DateTime lunes, double previstoAltos = 0.25, bool conModelo = true)
        {
            // 20 recibos altos (vuelven 5) y 180 bajos al 1 % (vuelven 2).
            for (int i = 0; i < 20; i++)
            {
                yield return new PrediccionResueltaImpago { FechaGiro = lunes.AddDays(i % 5), Probabilidad = conModelo ? previstoAltos : (double?)null, ProbabilidadRegla = 0.2, Devuelto = i < 5 };
            }
            for (int i = 0; i < 180; i++)
            {
                yield return new PrediccionResueltaImpago { FechaGiro = lunes.AddDays(i % 5), Probabilidad = conModelo ? 0.01 : (double?)null, ProbabilidadRegla = 0.01, Devuelto = i < 2 };
            }
        }

        [TestMethod]
        public void Fiabilidad_Lunes()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaFiabilidadImpagos.Lunes(new DateTime(2026, 11, 22)), "domingo");
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaFiabilidadImpagos.Lunes(new DateTime(2026, 11, 16)));
        }

        [TestMethod]
        public void Fiabilidad_CuatroSemanasSeguidasCumpliendo_Fiable()
        {
            // Hoy es domingo 22/11/2026: la última semana cerrada es la del lunes 28/09 (su domingo + 45 días ya pasó).
            DateTime ultima = new DateTime(2026, 9, 28);
            var resueltas = new List<PrediccionResueltaImpago>();
            for (int s = 0; s < 10; s++) { resueltas.AddRange(Semana(ultima.AddDays(-7 * s))); }
            resueltas.AddRange(Semana(ultima.AddDays(7), previstoAltos: 0.9)); // semana sin cerrar: no cuenta

            FiabilidadImpagos f = ReglaFiabilidadImpagos.Evaluar(resueltas, HOY, 6);

            Assert.IsTrue(f.Fiable, f.Motivo);
            Assert.AreEqual(6, f.Semanas.Count);
            Assert.AreEqual(ultima, f.Semanas.Last().Semana);
            Assert.IsTrue(f.Semanas.Last().Cumple);
            Assert.AreEqual(1.0, f.Semanas.Last().CalibracionAltos.Value, 1e-9);
            // La primera semana lleva 7 devoluciones; la ventana llega a 30 en la 5.ª: cumplen de la 5.ª a la 10.ª (6 seguidas).
            Assert.AreEqual(6, f.SemanasSeguidas);
            Assert.AreEqual(ultima.AddDays(-7 * 2).AddDays(6), f.FiableDesde, "el domingo de la 4.ª semana seguida");
        }

        [TestMethod]
        public void Fiabilidad_MalCalibradoEnLosTramosAltos_NoEsFiable()
        {
            DateTime ultima = new DateTime(2026, 9, 28);
            var resueltas = new List<PrediccionResueltaImpago>();
            for (int s = 0; s < 10; s++) { resueltas.AddRange(Semana(ultima.AddDays(-7 * s), previstoAltos: 0.5)); }

            FiabilidadImpagos f = ReglaFiabilidadImpagos.Evaluar(resueltas, HOY, 4);

            Assert.IsFalse(f.Fiable);
            Assert.IsNull(f.FiableDesde);
            Assert.AreEqual(0, f.SemanasSeguidas);
            StringAssert.Contains(f.Motivo, "fuera de ±25 %");
        }

        [TestMethod]
        public void Fiabilidad_UnaSemanaMalaRompeLaRacha()
        {
            DateTime ultima = new DateTime(2026, 9, 28);
            var resueltas = new List<PrediccionResueltaImpago>();
            for (int s = 0; s < 14; s++) { resueltas.AddRange(Semana(ultima.AddDays(-7 * s))); }
            // La última semana no tiene ningún recibo con predicción del modelo.
            resueltas.RemoveAll(p => p.FechaGiro >= ultima);
            resueltas.AddRange(Semana(ultima, conModelo: false));

            FiabilidadImpagos f = ReglaFiabilidadImpagos.Evaluar(resueltas, HOY, 4);

            Assert.IsFalse(f.Fiable);
            Assert.AreEqual(0, f.SemanasSeguidas);
            Assert.IsTrue(f.Semanas[f.Semanas.Count - 2].Cumple, "la anterior sí cumplía");
        }

        [TestMethod]
        public void Fiabilidad_SinPrediccionesDelModelo_LoDice()
        {
            FiabilidadImpagos f = ReglaFiabilidadImpagos.Evaluar(Semana(new DateTime(2026, 9, 28), conModelo: false), HOY, 4);

            Assert.IsFalse(f.Fiable);
            StringAssert.Contains(f.Motivo, "no hay predicciones del modelo");
        }

        #endregion

        #region Job de entrenamiento

        private sealed class EvaluacionFalsa : IEvaluacionModeloImpagos
        {
            public bool DatosSuficientes { get; set; }
            public string Motivo { get; set; }
            public int Ejemplos { get; set; }
            public int EjemplosEntrenamiento { get; set; }
            public int EjemplosValidacion { get; set; }
            public DateTime ValidacionDesde { get; set; }
            public MetricasImpagos Modelo { get; set; }
            public MetricasImpagos Regla { get; set; }
            public CalibradorPlatt Calibrador { get; set; } = new CalibradorPlatt { A = 2, B = -3 };
            public PesosReglaImpagos ReglaFinal { get; set; }
            public int Entrenados { get; private set; }

            public MetadatosModeloImpagos EntrenarFinalYGuardar(string rutaZip, DateTime fecha, string origen)
            {
                Entrenados++;
                File.WriteAllText(rutaZip, "zip de prueba");
                return new MetadatosModeloImpagos { Fecha = fecha, Version = MetadatosModeloImpagos.VERSION, PlattA = Calibrador.A, PlattB = Calibrador.B, Origen = origen };
            }
        }

        private ModeloImpagosRemesasJobsService Job(IRepositorioModeloImpagos repositorio, IEvaluacionModeloImpagos evaluacion, List<Exception> errores, AlmacenModeloImpagos almacen = null)
        {
            IEntrenadorModeloImpagos entrenador = A.Fake<IEntrenadorModeloImpagos>();
            _ = A.CallTo(() => entrenador.Evaluar(A<IReadOnlyList<EjemploImpago>>._, A<DateTime>._)).Returns(evaluacion);
            return new ModeloImpagosRemesasJobsService(repositorio, entrenador, almacen ?? new AlmacenModeloImpagos(carpeta), () => HOY.AddHours(4), errores.Add, () => { });
        }

        [TestMethod]
        public void Job_SinLaTabla_NoHaceNadaYAvisaUnaVez()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(false);
            var errores = new List<Exception>();
            ModeloImpagosRemesasJobsService job = Job(repositorio, new EvaluacionFalsa(), errores);

            ResultadoEntrenamientoModeloImpagos primero = job.Ejecutar("test");
            _ = job.Ejecutar("test");

            Assert.IsFalse(primero.Hecho);
            Assert.AreEqual(ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA, primero.Mensaje);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.RegistrarEntrenamiento(A<EntrenamientoModeloImpagos>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Job_DatosInsuficientes_ApuntaLaFilaYGuardaLaReglaPeroNoModelo()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).Returns(new HistoricoImpagos());
            var evaluacion = new EvaluacionFalsa { DatosSuficientes = false, Motivo = "datos insuficientes: pocos", Ejemplos = 12, ReglaFinal = new PesosReglaImpagos { LogOddsBase = -3, Ejemplos = 12 } };
            var errores = new List<Exception>();
            EntrenamientoModeloImpagos fila = null;
            _ = A.CallTo(() => repositorio.RegistrarEntrenamiento(A<EntrenamientoModeloImpagos>._)).Invokes((EntrenamientoModeloImpagos f) => fila = f);
            var almacen = new AlmacenModeloImpagos(carpeta);

            ResultadoEntrenamientoModeloImpagos resultado = Job(repositorio, evaluacion, errores, almacen).Ejecutar("test");

            Assert.IsTrue(resultado.Hecho);
            Assert.IsFalse(resultado.Promovido);
            Assert.AreEqual(0, errores.Count);
            Assert.IsNotNull(fila);
            Assert.IsFalse(fila.ModeloActivo);
            Assert.IsNull(fila.AucModelo);
            StringAssert.StartsWith(fila.Motivo, "no se entrena: datos insuficientes");
            Assert.IsTrue(almacen.HayRegla, "la regla se guarda siempre");
            Assert.IsFalse(almacen.HayModelo);
            Assert.AreEqual(0, evaluacion.Entrenados);
            A.CallTo(() => repositorio.LeerHistorico(HOY.AddYears(-ModeloImpagosRemesasJobsService.ANIOS_HISTORICO))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Job_PasaLaPuerta_PromueveElModeloYApuntaLasMetricasDeLosDos()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).Returns(new HistoricoImpagos());
            _ = A.CallTo(() => repositorio.UltimoActivo()).Returns((EntrenamientoModeloImpagos)null);
            var evaluacion = new EvaluacionFalsa
            {
                DatosSuficientes = true, Motivo = "modelo…", Ejemplos = 5000, EjemplosEntrenamiento = 4000, EjemplosValidacion = 1000,
                ValidacionDesde = HOY.AddMonths(-7), Modelo = M(0.88, 45), Regla = M(0.85, 41), ReglaFinal = new PesosReglaImpagos { LogOddsBase = -3 }
            };
            evaluacion.Modelo.Tramos.Add(new TramoCalibracionImpagos { Tramo = "≥ 30 %", Recibos = 10, Devueltos = 4 });
            var errores = new List<Exception>();
            EntrenamientoModeloImpagos fila = null;
            _ = A.CallTo(() => repositorio.RegistrarEntrenamiento(A<EntrenamientoModeloImpagos>._)).Invokes((EntrenamientoModeloImpagos f) => fila = f);
            var almacen = new AlmacenModeloImpagos(carpeta);

            ResultadoEntrenamientoModeloImpagos resultado = Job(repositorio, evaluacion, errores, almacen).Ejecutar("test");

            Assert.IsTrue(resultado.Promovido, resultado.Mensaje);
            Assert.AreEqual(0, errores.Count);
            Assert.IsTrue(fila.ModeloActivo);
            Assert.AreEqual(0.88, fila.AucModelo);
            Assert.AreEqual(0.85, fila.AucRegla);
            Assert.AreEqual(45.0, fila.Captado10Modelo);
            Assert.AreEqual(100, fila.DevueltosValidacion);
            Assert.AreEqual(2.0, fila.PlattA);
            StringAssert.Contains(fila.TramosModelo, "≥ 30 %");
            StringAssert.StartsWith(fila.Motivo, "promovido: ");
            Assert.IsTrue(almacen.HayModelo);
            Assert.AreEqual(-3.0, almacen.LeerDatos().PlattB);
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length);
        }

        [TestMethod]
        public void Job_PeorQueLaRegla_NoPromuevePeroApunta()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).Returns(new HistoricoImpagos());
            var evaluacion = new EvaluacionFalsa { DatosSuficientes = true, Modelo = M(0.80), Regla = M(0.85), ReglaFinal = new PesosReglaImpagos() };
            EntrenamientoModeloImpagos fila = null;
            _ = A.CallTo(() => repositorio.RegistrarEntrenamiento(A<EntrenamientoModeloImpagos>._)).Invokes((EntrenamientoModeloImpagos f) => fila = f);
            var almacen = new AlmacenModeloImpagos(carpeta);

            ResultadoEntrenamientoModeloImpagos resultado = Job(repositorio, evaluacion, new List<Exception>(), almacen).Ejecutar("test");

            Assert.IsTrue(resultado.Hecho);
            Assert.IsFalse(resultado.Promovido);
            Assert.IsFalse(fila.ModeloActivo);
            StringAssert.StartsWith(fila.Motivo, "no promovido: ordena peor que la regla");
            Assert.IsFalse(almacen.HayModelo);
            Assert.AreEqual(0, evaluacion.Entrenados);
        }

        [TestMethod]
        public void Job_SiFallaLaLectura_NoLanzaYLoRegistra()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).Throws(new InvalidOperationException("sin conexión"));
            var errores = new List<Exception>();

            ResultadoEntrenamientoModeloImpagos resultado = Job(repositorio, new EvaluacionFalsa(), errores).Ejecutar("test");

            Assert.IsFalse(resultado.Hecho);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "El modelo guardado no se toca");
        }

        [TestMethod]
        public void Job_Programado_SoloDeMadrugada()
        {
            Assert.IsTrue(ModeloImpagosRemesasJobsService.TocaEntrenar(HOY.AddHours(4).AddMinutes(40)));
            Assert.IsFalse(ModeloImpagosRemesasJobsService.TocaEntrenar(HOY.AddHours(9)));
        }

        #endregion

        #region Sombra

        private sealed class PredictorFalso : IPredictorImpagos
        {
            public bool HayAlgo { get; set; } = true;
            public List<IList<RasgosImpago>> Llamadas { get; } = new List<IList<RasgosImpago>>();
            public Func<RasgosImpago, PrediccionRiesgoImpago> Como { get; set; } = r => new PrediccionRiesgoImpago
            {
                Probabilidad = r.DevolucionesTotales > 0 ? 0.31234567 : 0.02,
                ProbabilidadRegla = 0.05,
                VersionModelo = "impagos-1 2026-11-22",
                Motivos = r.DevolucionesTotales > 0 ? new List<string> { "devolvió un recibo hace 20 días", "ha devuelto 1 vez en el último año" } : new List<string>()
            };

            public List<PrediccionRiesgoImpago> Predecir(IList<RasgosImpago> rasgos)
            {
                Llamadas.Add(rasgos);
                return rasgos.Select(Como).ToList();
            }
        }

        [TestMethod]
        public void Sombra_ApuntaUnaFilaPorEfectoConLoQueSeSabiaEseDia()
        {
            DateTime dia = HOY;
            ReciboGirado efectoBueno = Recibo(dia, cliente: "100", importe: 80);
            ReciboGirado efectoMalo = Recibo(dia, cliente: "200", importe: 300);
            ReciboGirado anteriorDevuelto = Recibo(dia.AddDays(-30), cliente: "200", documento: "NV500");
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerEfectosRemesa("1", 10950)).Returns(new List<ReciboGirado> { efectoBueno, efectoMalo });
            _ = A.CallTo(() => repositorio.LeerHistorialClientes("1", A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HistoricoImpagos
            {
                Recibos = new List<ReciboGirado> { anteriorDevuelto, efectoBueno, efectoMalo },
                Impagados = new List<ImpagadoLeido> { new ImpagadoLeido { Empresa = "1", Cliente = "200", Documento = "NV500", Efecto = "1", Fecha = dia.AddDays(-20), Concepto = "Saldo insuficiente." } }
            });
            _ = A.CallTo(() => repositorio.LeerNivelGeneral(dia)).Returns(new NivelGeneralImpagos(null, null));
            List<FilaPrediccionImpago> guardadas = null;
            _ = A.CallTo(() => repositorio.GuardarPredicciones(A<IReadOnlyCollection<FilaPrediccionImpago>>._))
                .ReturnsLazily((IReadOnlyCollection<FilaPrediccionImpago> f) => { guardadas = f.ToList(); return f.Count; });
            var predictor = new PredictorFalso();
            var errores = new List<Exception>();

            int apuntadas = new SombraImpagosRemesa(repositorio, predictor, () => dia.AddHours(10), errores.Add).RegistrarRemesa("1", 10950);

            Assert.AreEqual(2, apuntadas);
            Assert.AreEqual(0, errores.Count);
            Assert.AreEqual(2, guardadas.Count);
            FilaPrediccionImpago mala = guardadas.Single(f => f.Cliente == "200");
            Assert.AreEqual(10950, mala.Remesa);
            Assert.AreEqual(efectoMalo.NumOrden, mala.NumOrden);
            Assert.AreEqual(300m, mala.Importe);
            Assert.AreEqual(dia, mala.FechaGiro);
            Assert.AreEqual(0.312346, mala.Probabilidad);
            Assert.AreEqual(0.05, mala.ProbabilidadRegla);
            Assert.AreEqual("impagos-1 2026-11-22", mala.VersionModelo);
            Assert.AreEqual("devolvió un recibo hace 20 días; ha devuelto 1 vez en el último año", mala.MotivosRiesgo);
            RasgosImpago rasgos = JsonConvert.DeserializeObject<RasgosImpago>(mala.Rasgos);
            Assert.AreEqual(20f, rasgos.DiasDesdeUltimaDevolucion);
            Assert.AreEqual(1f, rasgos.RecibosAnteriores, "el propio efecto no cuenta como historia");
            Assert.IsNull(guardadas.Single(f => f.Cliente == "100").MotivosRiesgo);
            A.CallTo(() => repositorio.LeerHistorico(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sombra_SinLaTabla_NoLeeNadaYAvisaUnaVez()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(false);
            var errores = new List<Exception>();
            var sombra = new SombraImpagosRemesa(repositorio, new PredictorFalso(), () => HOY, errores.Add);

            Assert.AreEqual(0, sombra.RegistrarRemesa("1", 10950));
            Assert.AreEqual(0, sombra.RegistrarRemesa("1", 10951));

            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.LeerEfectosRemesa(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sombra_SinReglaNiModeloGuardados_NoHaceNada()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            var errores = new List<Exception>();

            int apuntadas = new SombraImpagosRemesa(repositorio, new PredictorFalso { HayAlgo = false }, () => HOY, errores.Add).RegistrarRemesa("1", 10950);

            Assert.AreEqual(0, apuntadas);
            Assert.AreEqual(0, errores.Count);
            A.CallTo(() => repositorio.LeerEfectosRemesa(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sombra_SiAlgoFalla_NoLanzaYLoRegistra()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerEfectosRemesa(A<string>._, A<int>._)).Throws(new TimeoutException("tarda"));
            var errores = new List<Exception>();

            int apuntadas = new SombraImpagosRemesa(repositorio, new PredictorFalso(), () => HOY, errores.Add).RegistrarRemesa("1", 10950);

            Assert.AreEqual(0, apuntadas);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "remesa 10950");
        }

        [TestMethod]
        public void Sombra_AlCrearRemesa_SoloEncolaYNuncaLanza()
        {
            var encoladas = new List<string>();
            SombraImpagosRemesa.Encolador = (empresa, remesa) => encoladas.Add(empresa + "/" + remesa);
            SombraImpagosRemesa.AlCrearRemesa("1  ", 10950);
            SombraImpagosRemesa.AlCrearRemesa("1", 0);
            SombraImpagosRemesa.AlCrearRemesa(null, 10951);
            CollectionAssert.AreEqual(new[] { "1/10950" }, encoladas);

            SombraImpagosRemesa.Encolador = (empresa, remesa) => throw new InvalidOperationException("Hangfire caído");
            SombraImpagosRemesa.AlCrearRemesa("1", 10952);
            SombraImpagosRemesa.AlCrearRemesa("1", 10953);
        }

        [TestMethod]
        public void Resultado_ResuelvePorLotesHastaAcabar()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            List<long> primerLote = Enumerable.Range(1, ResultadoImpagosRemesasJobsService.LOTE).Select(i => (long)i * 2).ToList();
            _ = A.CallTo(() => repositorio.LeerPendientes(0, ResultadoImpagosRemesasJobsService.LOTE)).Returns(primerLote);
            _ = A.CallTo(() => repositorio.LeerPendientes(1000, ResultadoImpagosRemesasJobsService.LOTE)).Returns(new List<long> { 1001, 1007 });
            _ = A.CallTo(() => repositorio.Resolver(2, 1000, HOY)).Returns(40);
            _ = A.CallTo(() => repositorio.Resolver(1001, 1007, HOY)).Returns(1);

            int resueltas = ResultadoImpagosRemesasJobsService.Resolver(repositorio, HOY);

            Assert.AreEqual(41, resueltas);
            A.CallTo(() => repositorio.LeerPendientes(A<long>._, A<int>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Resultado_SinLaTabla_Nada()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(false);

            Assert.AreEqual(0, ResultadoImpagosRemesasJobsService.Resolver(repositorio, HOY));
            A.CallTo(() => repositorio.LeerPendientes(A<long>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Interruptor_SoloElUnoEnciende()
        {
            Assert.IsTrue(InterruptorEnsenarImpagos.Interpretar("1"));
            Assert.IsTrue(InterruptorEnsenarImpagos.Interpretar(" 1 "));
            Assert.IsFalse(InterruptorEnsenarImpagos.Interpretar("0"));
            Assert.IsFalse(InterruptorEnsenarImpagos.Interpretar(null));
            Assert.IsFalse(InterruptorEnsenarImpagos.Interpretar("true"));
        }

        [TestMethod]
        public void Comparacion_MideLaSombraDeLasSemanasPedidasYDiceSiEsFiable()
        {
            DateTime ultima = new DateTime(2026, 9, 28);
            var resueltas = new List<PrediccionResueltaImpago>();
            for (int s = 0; s < 10; s++) { resueltas.AddRange(Semana(ultima.AddDays(-7 * s))); }
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.UltimoEntrenamiento()).Returns(new EntrenamientoModeloImpagos { Id = 7, AucModelo = 0.88 });
            _ = A.CallTo(() => repositorio.LeerCuentaSombra()).Returns(new CuentaSombraImpagos { Predicciones = 2600, Pendientes = 600 });
            _ = A.CallTo(() => repositorio.LeerResueltas(A<DateTime>._)).Returns(resueltas);

            ComparacionModeloImpagosDTO dto = ComparacionModeloImpagos.Construir(repositorio, HOY, 9, modeloGuardado: true, reglaGuardada: true, ensenar: false);

            Assert.AreEqual(9, dto.Semanas);
            Assert.AreEqual(7, dto.UltimoEntrenamiento.Id);
            Assert.AreEqual(2600, dto.Sombra.Predicciones);
            Assert.AreEqual(600, dto.Sombra.Pendientes);
            // 9 semanas atrás desde el 22/11 → desde el lunes 14/09: entran las semanas del 14, 21 y 28 de septiembre.
            Assert.AreEqual(600, dto.Sombra.ResueltasConModelo);
            Assert.AreEqual(25.0, dto.Sombra.Modelo.Tramos[2].RealPorcentaje);
            Assert.AreEqual(25.0, dto.Sombra.Modelo.Tramos[2].PrevistoPorcentaje);
            Assert.AreEqual(20.0, dto.Sombra.Regla.Tramos[2].PrevistoPorcentaje);
            Assert.IsTrue(dto.Fiable, dto.Veredicto);
            Assert.IsFalse(dto.Ensenar);
            Assert.AreEqual(9, dto.SemanasEvaluadas.Count);
        }

        [TestMethod]
        public void Comparacion_SemanasFueraDeRango_SeAcotan()
        {
            IRepositorioModeloImpagos repositorio = A.Fake<IRepositorioModeloImpagos>();
            _ = A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerCuentaSombra()).Returns(new CuentaSombraImpagos());
            _ = A.CallTo(() => repositorio.LeerResueltas(A<DateTime>._)).Returns(new List<PrediccionResueltaImpago>());

            Assert.AreEqual(1, ComparacionModeloImpagos.Construir(repositorio, HOY, 0, false, false, false).Semanas);
            ComparacionModeloImpagosDTO dto = ComparacionModeloImpagos.Construir(repositorio, HOY, 5000, false, false, false);
            Assert.AreEqual(ComparacionModeloImpagos.SEMANAS_MAXIMO, dto.Semanas);
            Assert.IsFalse(dto.Fiable);
            Assert.AreEqual(0, dto.Sombra.Modelo.Recibos);
        }

        #endregion

        #region Entrenamiento real (LightGBM)

        /// <summary>300 clientes con un recibo al mes durante 5 años: 45 «malos» devuelven mucho (y más si acaban de devolver).</summary>
        private List<ReciboGirado> Sinteticos()
        {
            var azar = new Random(896);
            var recibos = new List<ReciboGirado>();
            for (int c = 0; c < 300; c++)
            {
                bool malo = c % 100 < 15;
                DateTime? ultimaDevolucion = null;
                for (DateTime giro = HOY.AddYears(-5).AddDays(c % 28); giro < HOY; giro = giro.AddMonths(1))
                {
                    double p = malo ? 0.25 : 0.01;
                    if (ultimaDevolucion.HasValue && (giro - ultimaDevolucion.Value).Days <= 90)
                    {
                        p = 0.6;
                    }
                    bool vuelve = azar.NextDouble() < p;
                    DateTime? impago = vuelve ? giro.AddDays(3 + azar.Next(0, 8)) : (DateTime?)null;
                    recibos.Add(Recibo(giro, cliente: "C" + c, importe: 50 + azar.Next(0, 500), impago: impago));
                    if (vuelve)
                    {
                        ultimaDevolucion = impago;
                    }
                }
            }
            return recibos;
        }

        [TestMethod]
        public void EntrenamientoReal_EntrenaCalibraYPredice_ElQueAcabaDeDevolverTieneMasRiesgo()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo entrena en 64 bits");
            }
            _ = CargadorLightGbmNativo.Asegurar();
            List<ReciboGirado> recibos = Sinteticos();
            List<EjemploImpago> ejemplos = ConstructorRasgosImpagos.Construir(recibos, null, HOY, HOY.AddYears(-4));

            IEvaluacionModeloImpagos evaluacion = new EntrenadorModeloImpagos(minimoHoja: 20, minimoCategoria: 50).Evaluar(ejemplos, HOY);

            Assert.IsTrue(evaluacion.DatosSuficientes, evaluacion.Motivo);
            Assert.IsTrue(evaluacion.Modelo.Devueltos >= PuertaCalidadImpagos.MINIMO_DEVUELTOS);
            Assert.IsTrue(evaluacion.Modelo.Auc > 0.75, "el modelo debería ordenar bien estos datos: " + evaluacion.Motivo);
            Assert.IsTrue(evaluacion.Regla.Auc > 0.70, "y la regla también: " + evaluacion.Motivo);
            Assert.IsTrue(evaluacion.Modelo.CalibracionAltos > 0.6 && evaluacion.Modelo.CalibracionAltos < 1.6, "calibración razonable: " + evaluacion.Motivo);
            Assert.IsNotNull(evaluacion.ReglaFinal);

            var almacen = new AlmacenModeloImpagos(carpeta);
            almacen.GuardarRegla(evaluacion.ReglaFinal);
            string temporal = almacen.NuevaRutaTemporal();
            MetadatosModeloImpagos datos = evaluacion.EntrenarFinalYGuardar(temporal, HOY, "test");
            almacen.Promover(temporal, datos);
            Assert.IsTrue(almacen.HayModelo && almacen.HayRegla);

            var errores = new List<Exception>();
            var predictor = new PredictorImpagos(almacen, errores.Add);
            var bueno = new RasgosImpago { MotivoUltima = MotivosImpago.NINGUNA, FormaVenta = "DIR", Vendedor = "NV", Entidad = "2100", DiasDesdeUltimaDevolucion = -1, RecibosAnteriores = 40, Recibos12Meses = 12, TasaDevolucionCliente = 0, AntiguedadDias = 1200, RecibosUltimos45 = 1, Importe = 200, RelacionImporteHabitual = 1, PrimerGiroCuenta = 0, NumeroEfecto = 1, DiasFacturaAGiro = 30, DiaMes = 12, Mes = 11, DiaSemana = 4, NivelGeneral14 = -1, NivelGeneral30 = -1 };
            var malo = new RasgosImpago { MotivoUltima = MotivosImpago.SALDO, FormaVenta = "DIR", Vendedor = "NV", Entidad = "2100", DiasDesdeUltimaDevolucion = 25, Devoluciones3Meses = 2, Devoluciones12Meses = 5, DevolucionesTotales = 14, RecibosAnteriores = 40, Recibos12Meses = 12, TasaDevolucionCliente = 0.35f, AntiguedadDias = 1200, RecibosUltimos45 = 1, Importe = 200, RelacionImporteHabitual = 1, PrimerGiroCuenta = 0, NumeroEfecto = 1, DiasFacturaAGiro = 30, DiaMes = 12, Mes = 11, DiaSemana = 4, NivelGeneral14 = -1, NivelGeneral30 = -1 };

            List<PrediccionRiesgoImpago> predicciones = predictor.Predecir(new List<RasgosImpago> { bueno, malo });

            Assert.AreEqual(0, errores.Count, errores.FirstOrDefault()?.ToString());
            Assert.IsTrue(predicciones[0].Probabilidad.HasValue && predicciones[1].Probabilidad.HasValue);
            Assert.IsTrue(predicciones[0].Probabilidad < 0.06, $"bueno {predicciones[0].Probabilidad}");
            Assert.IsTrue(predicciones[1].Probabilidad > 0.30, $"malo {predicciones[1].Probabilidad}");
            Assert.IsTrue(predicciones[1].ProbabilidadRegla > predicciones[0].ProbabilidadRegla * 3, "la regla también lo distingue");
            Assert.AreEqual("impagos-1 2026-11-22", predicciones[1].VersionModelo);
            Assert.IsTrue(predicciones[1].Motivos.Count >= 1 && predicciones[1].Motivos.Count <= 2, "causas: " + string.Join("; ", predicciones[1].Motivos));
            Assert.IsTrue(predicciones[1].Motivos.Any(m => m.Contains("devuelto") || m.Contains("devolvió")), string.Join("; ", predicciones[1].Motivos));
            Assert.AreEqual("Modelo", predicciones[1].OrigenMotivos, "las causas salen de la contribución de cada rasgo en el árbol");
        }

        [TestMethod]
        public void Entrenador_ConPocosDatos_LoDiceYAunAsiDejaLaRegla()
        {
            var ejemplos = new List<EjemploImpago>();
            for (int i = 0; i < 200; i++) { ejemplos.Add(Ejemplo(i % 10 == 0, i % 2 == 0 ? -1 : 20, HOY.AddDays(-60 - i))); }

            IEvaluacionModeloImpagos evaluacion = new EntrenadorModeloImpagos().Evaluar(ejemplos, HOY);

            Assert.IsFalse(evaluacion.DatosSuficientes);
            StringAssert.Contains(evaluacion.Motivo, "datos insuficientes");
            Assert.AreEqual(200, evaluacion.Ejemplos);
            Assert.IsNotNull(evaluacion.ReglaFinal);
            _ = Assert.ThrowsException<InvalidOperationException>(() => evaluacion.EntrenarFinalYGuardar(Path.Combine(carpeta, "x.zip"), HOY, "test"));
        }

        [TestMethod]
        public void Predictor_SoloConLaRegla_DaLaDeLaReglaYSusCausas()
        {
            var ejemplos = new List<EjemploImpago>();
            for (int i = 0; i < 100; i++) { ejemplos.Add(Ejemplo(i < 2, -1)); }
            for (int i = 0; i < 20; i++) { ejemplos.Add(Ejemplo(i < 8, 10)); }
            var almacen = new AlmacenModeloImpagos(carpeta);
            almacen.GuardarRegla(ReglaImpagos.Ajustar(ejemplos, HOY));
            var predictor = new PredictorImpagos(almacen, _ => Assert.Fail("no debería registrar nada"));

            Assert.IsTrue(predictor.HayAlgo);
            PrediccionRiesgoImpago p = predictor.Predecir(new List<RasgosImpago> { new RasgosImpago { DiasDesdeUltimaDevolucion = 10, MotivoUltima = MotivosImpago.SALDO, NivelGeneral14 = -1, DiaMes = 12, Importe = 100 } }).Single();

            Assert.IsNull(p.Probabilidad);
            Assert.IsNull(p.VersionModelo);
            Assert.IsTrue(p.ProbabilidadRegla > 0.15);
            CollectionAssert.AreEqual(new[] { "devolvió un recibo hace 10 días (saldo insuficiente)" }, p.Motivos);
            Assert.AreEqual("Regla", p.OrigenMotivos);
        }

        [TestMethod]
        public void Predictor_SinNadaGuardado_NoHayAlgo()
        {
            var predictor = new PredictorImpagos(new AlmacenModeloImpagos(carpeta), _ => { });

            Assert.IsFalse(predictor.HayAlgo);
            PrediccionRiesgoImpago p = predictor.Predecir(new List<RasgosImpago> { new RasgosImpago() }).Single();
            Assert.IsNull(p.Probabilidad);
            Assert.IsNull(p.ProbabilidadRegla);
        }

        #endregion

        #region SQL (contrato con el extracto)

        [TestMethod]
        public void Sql_ElReciboEsLaCarteraConRemesaYElImpagadoElApunteDelMotivo()
        {
            StringAssert.Contains(RepositorioModeloImpagos.SQL_RECIBOS, "e.TipoApunte = '2'");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_RECIBOS, "e.Remesa IS NOT NULL");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_RECIBOS, "WITH (NOLOCK)");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_FILTRO_HISTORICO, "e.FormaPago = 'RCB'");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_IMPAGADOS, "e.TipoApunte = '4'");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_IMPAGADOS, "NOT LIKE 'Gastos Impagado%'");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_RESOLVER, "NOT LIKE 'Gastos Impagado%'");
            StringAssert.Contains(RepositorioModeloImpagos.SQL_RESOLVER, "p.Devuelto IS NULL");
            Assert.IsFalse(RepositorioModeloImpagos.SQL_RESOLVER.Contains("TRY_CONVERT") || RepositorioModeloImpagos.SQL_RECIBOS.Contains("STRING_SPLIT"));
        }

        #endregion
    }
}
