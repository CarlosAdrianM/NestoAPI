using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models.Clientes;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#866 (corte 2): el motivo explicado por el modelo (contribución de cada rasgo del LightGBM).</summary>
    [TestClass]
    public class ExplicacionModeloContactoTests
    {
        private static readonly DateTime Dia = new DateTime(2026, 6, 15, 10, 30, 0); // lunes

        private static string RutaModeloMuestra()
        {
            string[] candidatas =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recursos", "modelo_llamadas_muestra_MPP_36m.zip"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Recursos", "modelo_llamadas_muestra_MPP_36m.zip")
            };
            string ruta = candidatas.FirstOrDefault(File.Exists);
            Assert.IsNotNull(ruta, "falta el zip de muestra en la salida de los tests");
            return ruta;
        }

        private static List<ModeloContactoEntrada> Entradas()
        {
            var bueno = new HistorialContacto("15191/0",
                Enumerable.Range(1, 30).Select(i => new PedidoDiaContacto(Dia.Date.AddDays(-12 * i), 150)),
                Enumerable.Range(1, 20).Select(i => new RapportContactoCrudo(Dia.AddDays(-12 * i - 1), i % 2 == 0)),
                new[] { new VentaGrupoMesContacto(202605, "COSCRE", 40) });
            var nada = new HistorialContacto("2/0", null, null, null);
            return new List<ModeloContactoEntrada>
            {
                CalculadoraFeaturesContacto.Calcular(bueno, Dia, "Llamada"),
                CalculadoraFeaturesContacto.Calcular(nada, Dia, "Llamada")
            };
        }

        [TestMethod]
        public void Contribuciones_DelZipDeMuestra_UnaPorRasgoYCuadranConLaPuntuacion()
        {
            List<ModeloContactoEntrada> entradas = Entradas();

            List<Dictionary<string, float>> contribuciones = ModeloContacto.Contribuciones(entradas, RutaModeloMuestra());

            Assert.AreEqual(2, contribuciones.Count);
            CollectionAssert.IsSubsetOf(new[] { "TipoInteraccion", "Mes", "DiaSemana", "GrupoSubgrupoMasVendido", "DiasDesdeUltimoPedido", "TasaConversionCliente" },
                contribuciones[0].Keys.ToList(), string.Join(", ", contribuciones[0].Keys));
            Assert.IsTrue(contribuciones[0].Values.Any(v => Math.Abs(v) > 0f), "algún rasgo pesa");
            Assert.IsTrue(contribuciones[0].Values.Sum() > contribuciones[1].Values.Sum(),
                "el que compra cada 12 días suma más hacia el pedido que el que no tiene historial");
        }

        [TestMethod]
        public void Contribuciones_DelZipDeMuestra_DanUnaExplicacionLegible()
        {
            List<ModeloContactoEntrada> entradas = Entradas();
            List<Dictionary<string, float>> contribuciones = ModeloContacto.Contribuciones(entradas, RutaModeloMuestra());

            string explicacion = ExplicacionModeloContacto.Explicacion(contribuciones[0], entradas[0]);

            Assert.IsNotNull(explicacion, string.Join("; ", contribuciones[0].Select(c => $"{c.Key}={c.Value}")));
            StringAssert.StartsWith(explicacion, ExplicacionModeloContacto.PREFIJO);
        }

        private class RepositorioFalso : IRepositorioFeaturesContacto
        {
            private readonly List<HistorialContacto> historiales;
            public RepositorioFalso(List<HistorialContacto> historiales) { this.historiales = historiales; }
            public List<HistorialContacto> Leer(string vendedor, DateTime hoy) => historiales;
        }

        private static ProbabilidadesContactoModelo Probabilidades(Func<IList<ModeloContactoEntrada>, string, List<Dictionary<string, float>>> contribuciones,
            List<Exception> errores)
        {
            var historiales = new List<HistorialContacto>
            {
                new HistorialContacto("1/0", Enumerable.Range(1, 10).Select(i => new PedidoDiaContacto(Dia.Date.AddDays(-30 * i), 100)), null, null)
            };
            return new ProbabilidadesContactoModelo(new RepositorioFalso(historiales), () => Dia, RutaModeloMuestra(),
                registrarError: errores.Add, contribuciones: contribuciones);
        }

        [TestMethod]
        public void Probabilidades_LlevanLaExplicacionDelModelo()
        {
            var errores = new List<Exception>();
            ProbabilidadesContactoModelo probabilidades = Probabilidades((entradas, ruta) => entradas
                .Select(e => new Dictionary<string, float> { ["DiasDesdeUltimoPedido"] = 0.4f, ["Pedidos12Meses"] = 0.2f }).ToList(), errores);
            string vendedor = "T866A" + Guid.NewGuid().ToString("N").Substring(0, 4);

            PrediccionContacto prediccion = probabilidades.Calcular(vendedor, "Llamada", "", Dia)["1/0"];

            Assert.AreEqual("Sobre todo porque lleva 30 días sin pedir y ha hecho 10 pedidos en el último año", prediccion.ExplicacionModelo);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void Probabilidades_SiFallanLasContribuciones_SalenSinExplicacionYConProbabilidad()
        {
            var errores = new List<Exception>();
            ProbabilidadesContactoModelo probabilidades = Probabilidades((entradas, ruta) => throw new InvalidOperationException("ML"), errores);
            string vendedor = "T866B" + Guid.NewGuid().ToString("N").Substring(0, 4);

            PrediccionContacto prediccion = probabilidades.Calcular(vendedor, "Llamada", "", Dia)["1/0"];

            Assert.IsNull(prediccion.ExplicacionModelo);
            Assert.IsTrue(prediccion.Probabilidad > 0f);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Priorizar_LasDeProbabilidadLlevanLaExplicacionDetras()
        {
            var hoy = new DateTime(2026, 11, 16);
            var cliente = new ClienteCarteraContacto
            {
                Cliente = "1", Contacto = "0", Pedidos12Meses = 30, Pedidos24Meses = 30, Probabilidad = 0.7f, UltimoPedido = hoy.AddDays(-20),
                UltimoContacto = hoy.AddDays(-20), ExplicacionModelo = "Sobre todo porque lleva 20 días sin pedir"
            };

            SugerenciaContactoDTO sugerencia = new MotorSugerenciasContacto().Priorizar(new[] { cliente }, hoy).Single();

            Assert.AreEqual("Compra cada 12 días y lleva 20 días sin hablar con usted; probabilidad de pedido del 70 %. Sobre todo porque lleva 20 días sin pedir.",
                sugerencia.Motivo);
            Assert.AreEqual(OrigenesSugerenciaContacto.PROBABILIDAD, OrigenesSugerenciaContacto.DeMotivo(sugerencia.Motivo));
        }

        [TestMethod]
        public void Rasgo_QuitaElValorYElCodDelOneHot()
        {
            Assert.AreEqual("Mes", ExplicacionModeloContacto.Rasgo("MesCod.11"));
            Assert.AreEqual("GrupoSubgrupoMasVendido", ExplicacionModeloContacto.Rasgo("GrupoSubgrupoMasVendidoCod.COSCRE"));
            Assert.AreEqual("Pedidos12Meses", ExplicacionModeloContacto.Rasgo("Pedidos12Meses"));
        }

        [TestMethod]
        public void Explicacion_LasDosQueMasEmpujanConFraseYEnOrden()
        {
            var entrada = new ModeloContactoEntrada { DiasDesdeUltimoPedido = 64, TasaConversionCliente = 0.6f, ContactosPrevios = 12, Pedidos12Meses = 9 };
            var contribuciones = new Dictionary<string, float>
            {
                ["Pedidos12Meses"] = 0.1f,
                ["TasaConversionCliente"] = 0.3f,
                ["DiasDesdeUltimoPedido"] = 0.5f,
                ["SinHistorial"] = 0.9f,   // sin frase: no cuenta
                ["Mes"] = -0.8f            // empuja en contra: no cuenta
            };

            Assert.AreEqual("Sobre todo porque lleva 64 días sin pedir y suele pedir cuando se le contacta (el 60 % de las veces)",
                ExplicacionModeloContacto.Explicacion(contribuciones, entrada));
            Assert.AreEqual("Sobre todo porque lleva 64 días sin pedir", ExplicacionModeloContacto.Explicacion(contribuciones, entrada, 1));
            Assert.IsNull(ExplicacionModeloContacto.Explicacion(new Dictionary<string, float> { ["Mes"] = -1f }, entrada));
        }

        [TestMethod]
        public void Frase_PlantillaPorRasgo()
        {
            var e = new ModeloContactoEntrada
            {
                DiasDesdeUltimoPedido = 1000, DiasEntrePedidos = 30.4f, Pedidos12Meses = 12, Importe12Meses = 4321.5f, ImporteMedioPedido = 360,
                TendenciaImporte = 0.3f, TasaConversionCliente = 0.25f, ContactosPrevios = 0, PedidosMismoMesAnnoAnterior = 2, Mes = "11",
                DiaSemana = "1", TipoInteraccion = "Llamada", GrupoSubgrupoMasVendido = "COSCRE", EsPorLaTarde = 1
            };

            Assert.AreEqual("no ha pedido en dos años", ExplicacionModeloContacto.Frase("DiasDesdeUltimoPedido", e));
            Assert.AreEqual("suele pedir cada 30 días", ExplicacionModeloContacto.Frase("DiasEntrePedidos", e));
            Assert.AreEqual("ha hecho 12 pedidos en el último año", ExplicacionModeloContacto.Frase("Pedidos12Meses", e));
            Assert.AreEqual("ha comprado 4.322 € en el último año", ExplicacionModeloContacto.Frase("Importe12Meses", e));
            Assert.AreEqual("su pedido medio es de 360 €", ExplicacionModeloContacto.Frase("ImporteMedioPedido", e));
            Assert.AreEqual("está comprando más que hace seis meses", ExplicacionModeloContacto.Frase("TendenciaImporte", e));
            Assert.IsNull(ExplicacionModeloContacto.Frase("TasaConversionCliente", e), "sin contactos previos, la tasa es la de partida: no se dice");
            Assert.AreEqual("el año pasado por estas fechas hizo 2 pedidos", ExplicacionModeloContacto.Frase("PedidosMismoMesAnnoAnterior", e));
            Assert.AreEqual("noviembre suele ser buen mes para él", ExplicacionModeloContacto.Frase("Mes", e));
            Assert.AreEqual("el lunes es buen día para él", ExplicacionModeloContacto.Frase("DiaSemana", e));
            Assert.AreEqual("con él funciona bien el contacto por teléfono", ExplicacionModeloContacto.Frase("TipoInteraccion", e));
            Assert.AreEqual("lo que más compra (COSCRE) se pide mucho en esta época", ExplicacionModeloContacto.Frase("GrupoSubgrupoMasVendido", e));
            Assert.AreEqual("por la tarde es buen momento para él", ExplicacionModeloContacto.Frase("EsPorLaTarde", e));
            Assert.IsNull(ExplicacionModeloContacto.Frase("SinHistorial", e));
            e.DiaSemana = "7";
            Assert.AreEqual("el domingo es buen día para él", ExplicacionModeloContacto.Frase("DiaSemana", e));
        }

        [TestMethod]
        public void ConExplicacion_DetrasDelMotivoSinPasarDeLaLongitud()
        {
            const string motivo = "Compra cada 12 días y lleva 20 días sin hablar con usted; probabilidad de pedido del 65 %";
            const string explicacion = "Sobre todo porque lleva 64 días sin pedir y suele pedir cuando se le contacta (el 60 % de las veces)";

            Assert.AreEqual(motivo + ". " + explicacion + ".", ExplicacionModeloContacto.ConExplicacion(motivo, explicacion));
            Assert.AreEqual(motivo + ". Sobre todo porque lleva 64 días sin pedir.", ExplicacionModeloContacto.ConExplicacion(motivo, explicacion, reservar: 20),
                "si no cabe entera, la primera causa");
            string largo = new string('x', 170);
            Assert.AreEqual(largo, ExplicacionModeloContacto.ConExplicacion(largo, explicacion), "si no cabe ni la primera, el motivo como estaba");
            Assert.AreEqual(motivo, ExplicacionModeloContacto.ConExplicacion(motivo, null));
            string yaExplicado = ExplicacionModeloContacto.ConExplicacion(motivo, explicacion);
            Assert.AreEqual(yaExplicado, ExplicacionModeloContacto.ConExplicacion(yaExplicado, explicacion), "no se repite");
        }
    }
}
