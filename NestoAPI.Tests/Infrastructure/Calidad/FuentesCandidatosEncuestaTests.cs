using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (encuestas, E1): cada fuente con resultados simulados (filas de DataTable), el canal y la situación del cliente.</summary>
    [TestClass]
    public class FuentesCandidatosEncuestaTests
    {
        private static readonly DateTime EVENTO = new DateTime(2026, 11, 20, 12, 30, 0);

        /// <summary>Una tabla con esas columnas y filas (cada fila, valores en el mismo orden; null = DBNull).</summary>
        private static IDataReader Filas(string[] columnas, params object[][] filas)
        {
            var tabla = new DataTable();
            foreach (string c in columnas)
            {
                _ = tabla.Columns.Add(c, typeof(object));
            }
            foreach (object[] fila in filas)
            {
                _ = tabla.Rows.Add(fila.Select(v => v ?? DBNull.Value).ToArray());
            }
            return tabla.CreateDataReader();
        }

        // ------------------------------------------------------------ entrega

        [TestMethod]
        public void Entrega_DiaEsLaEntregaMasDosYEnlazaPedidoYEnvio()
        {
            var fuente = new FuenteEntregaPedido();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Pedido", "FechaEntrega", "Envio", "Cliente", "Contacto" },
                new object[] { 930001, EVENTO, 251000, "15000 ", "0  " }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(TemaEncuesta.PreparacionEntrega, c[0].Tema);
            Assert.AreEqual(new DateTime(2026, 11, 22), c[0].Dia);
            Assert.AreEqual("15000", c[0].Cliente);
            Assert.AreEqual("0", c[0].Contacto);
            Assert.AreEqual(930001, c[0].Pedido);
            Assert.AreEqual(251000, c[0].Envio);
            Assert.AreEqual("EntregaPedido", c[0].Origen);
            StringAssert.Contains(c[0].Motivo, "930001");
        }

        // ------------------------------------------------------------ vendedor

        private static readonly string[] COLUMNAS_PRIMER_PEDIDO =
        {
            "Pedido", "Fecha", "Cliente", "Contacto", "VendedorEstetica", "VendedorPeluqueria", "LineasPeluqueria", "LineasOtras",
            "PrevioEstetica", "PrevioPeluqueria", "NombreEstetica", "EstadoEstetica", "NombrePeluqueria", "EstadoPeluqueria"
        };

        private static List<CandidataEncuesta> PrimerPedido(string estetica, string peluqueria, int lineasPel, int lineasOtras, bool previoEst = false,
            bool previoPel = false, int estadoEst = 0, int estadoPel = 4)
        {
            var fuente = new FuenteVendedorPrimerPedido();
            return fuente.Construir(Filas(COLUMNAS_PRIMER_PEDIDO, new object[]
            {
                930002, EVENTO, "16000", "0", estetica, peluqueria, lineasPel, lineasOtras, previoEst, previoPel,
                "Paloma Sánchez", estadoEst, "Ana Pérez", estadoPel
            }), "1", fuente.RetrasoDias(null));
        }

        [TestMethod]
        public void PrimerPedido_SoloPeluqueria_PreguntaPorElDePeluqueria()
        {
            List<CandidataEncuesta> c = PrimerPedido("PA", "AP", lineasPel: 3, lineasOtras: 0);

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual("AP", c[0].Vendedor);
            Assert.AreEqual("Ana Pérez", c[0].NombreVendedor);
            Assert.AreEqual(AmbitosVendedorEncuesta.PELUQUERIA, c[0].AmbitoVendedor);
            Assert.AreEqual(ClaseCandidataEncuesta.PrimeraVez, c[0].Clase);
            Assert.AreEqual("Vendedor|AP", c[0].ClaveTema);
            Assert.AreEqual(EVENTO.Date.AddDays(3), c[0].Dia);
            StringAssert.Contains(c[0].Motivo, "de peluquería");
        }

        [TestMethod]
        public void PrimerPedido_Mixto_DosCandidatas()
        {
            List<CandidataEncuesta> c = PrimerPedido("PA", "AP", lineasPel: 1, lineasOtras: 2);

            Assert.AreEqual(2, c.Count);
            Assert.IsTrue(c.Any(x => x.Vendedor == "AP" && x.AmbitoVendedor == AmbitosVendedorEncuesta.PELUQUERIA));
            Assert.IsTrue(c.Any(x => x.Vendedor == "PA" && x.AmbitoVendedor == AmbitosVendedorEncuesta.ESTETICA && x.NombreVendedor == "Paloma Sánchez"));
        }

        [TestMethod]
        public void PrimerPedido_ConUnPedidoAnteriorDeEseVendedor_NoSale()
        {
            List<CandidataEncuesta> c = PrimerPedido("PA", "AP", lineasPel: 1, lineasOtras: 2, previoEst: true);

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual("AP", c[0].Vendedor);
        }

        [TestMethod]
        public void PrimerPedido_PeluqueriaSinVendedorDePeluqueria_NoSeLePreguntaAlDeEstetica()
        {
            List<CandidataEncuesta> c = PrimerPedido("PA", "", lineasPel: 2, lineasOtras: 0);

            Assert.AreEqual(0, c.Count);
        }

        [TestMethod]
        public void PrimerPedido_VendedorGeneralNV_Fuera()
        {
            List<CandidataEncuesta> c = PrimerPedido("NV", "", lineasPel: 0, lineasOtras: 2);

            Assert.AreEqual(0, c.Count);
        }

        [TestMethod]
        public void PrimerPedido_VendedorParaAnular_Fuera()
        {
            List<CandidataEncuesta> c = PrimerPedido("XX", "", lineasPel: 0, lineasOtras: 2, estadoEst: 99);

            Assert.AreEqual(0, c.Count);
        }

        [DataTestMethod]
        [DataRow("NV", 0, false)]
        [DataRow("nv ", 0, false)]
        [DataRow("", 0, false)]
        [DataRow("PA", 99, false)]
        [DataRow("PA", -1, false)]
        [DataRow("PA", 0, true)]
        [DataRow("PA", 9, true)]
        [DataRow("AP", 4, true)]
        public void VendedorEsPersona(string vendedor, int estado, bool esperado)
        {
            Assert.AreEqual(esperado, ReglaVendedorEncuesta.EsPersona(vendedor, estado));
        }

        [TestMethod]
        public void CambioFicha_Estetica_CandidataDeCambio()
        {
            var fuente = new FuenteVendedorCambioFicha();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Cliente", "Contacto", "Fecha", "Vendedor", "VendedorAnterior", "NombreVendedor", "EstadoVendedor" },
                new object[] { "17000", "0", EVENTO, "MJ", "PA", "María José", 0 }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(ClaseCandidataEncuesta.Cambio, c[0].Clase);
            Assert.AreEqual("MJ", c[0].Vendedor);
            Assert.AreEqual(AmbitosVendedorEncuesta.ESTETICA, c[0].AmbitoVendedor);
            Assert.AreEqual(EVENTO.Date.AddDays(14), c[0].Dia);
        }

        [TestMethod]
        public void CambioFicha_AlVendedorGeneral_NoSale()
        {
            var fuente = new FuenteVendedorCambioFicha();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Cliente", "Contacto", "Fecha", "Vendedor", "VendedorAnterior", "NombreVendedor", "EstadoVendedor" },
                new object[] { "17000", "0", EVENTO, "NV", "PA", "Nueva Visión", 0 }), "1", 14);

            Assert.AreEqual(0, c.Count);
        }

        [TestMethod]
        public void CambioFicha_Peluqueria_CandidataDelNuevoDePeluqueria()
        {
            var fuente = new FuenteVendedorPeluqueriaCambioFicha();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Cliente", "Contacto", "Fecha", "Vendedor", "VendedorAnterior", "NombreVendedor", "EstadoVendedor" },
                new object[] { "18000", "0", EVENTO, "AP", "RS", "Ana Pérez", 4 },
                new object[] { "18001", "0", EVENTO, "AP", "ap", "Ana Pérez", 4 }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(1, c.Count, "Si el anterior es el mismo, no es un cambio");
            Assert.AreEqual("18000", c[0].Cliente);
            Assert.AreEqual(AmbitosVendedorEncuesta.PELUQUERIA, c[0].AmbitoVendedor);
            Assert.AreEqual(ClaseCandidataEncuesta.Cambio, c[0].Clase);
            Assert.AreEqual("Vendedor|AP", c[0].ClaveTema);
        }

        // ------------------------------------------------------------ problema resuelto

        [TestMethod]
        public void NotaEntregaPendiente_ProblemaResueltoDelPedidoOriginal()
        {
            var fuente = new FuenteProblemaNotaEntregaPendiente();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Pedido", "Nota", "FechaEntrega", "Envio", "Cliente", "Contacto" },
                new object[] { 930003, 930050, EVENTO, 251001, "19000", "1" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(ClaseCandidataEncuesta.ProblemaResuelto, c[0].Clase);
            Assert.AreEqual(TemaEncuesta.ProblemaResuelto, c[0].Tema);
            Assert.AreEqual(930003, c[0].Pedido);
            Assert.AreEqual(EVENTO.Date.AddDays(2), c[0].Dia);
        }

        [TestMethod]
        public void ReenvioEntregado_ProblemaResuelto()
        {
            var fuente = new FuenteProblemaReenvioEntregado();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Pedido", "Envio", "EnvioOrigen", "FechaEntrega", "Cliente", "Contacto" },
                new object[] { 930004, 251010, 251002, EVENTO, "19001", "0" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(251010, c[0].Envio);
            Assert.AreEqual("dbo.EnviosAgenciaReenvios", fuente.TablaNecesaria);
            StringAssert.Contains(c[0].Motivo, "reenvío");
        }

        [TestMethod]
        public void BultosPendientesCerrados_ProblemaResuelto()
        {
            var fuente = new FuenteProblemaBultosPendientesCerrados();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Envio", "Pedido", "Cliente", "Contacto", "FechaCierre", "Cierre" },
                new object[] { 251003, 930005, "19002", "0", EVENTO, "EntregadoEnMano" },
                new object[] { 251004, null, null, null, EVENTO, "Perdido" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(1, c.Count, "Sin cliente no hay a quién preguntar");
            Assert.AreEqual("dbo.RepartoBultosPendientes", fuente.TablaNecesaria);
        }

        [TestMethod]
        public void Rectificativa_EnlazaLaFactura()
        {
            var fuente = new FuenteProblemaRectificativa();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Factura", "Fecha", "Cliente", "Contacto", "FacturaOriginal" },
                new object[] { "RV2600010", EVENTO.Date, "19003", "0", "NV2600100" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual("RV2600010", c[0].Factura);
            Assert.AreEqual(EVENTO.Date.AddDays(3), c[0].Dia);
            StringAssert.Contains(c[0].Motivo, "NV2600100");
        }

        // ------------------------------------------------------------ administración y cobro

        [TestMethod]
        public void PrimerGiro_PrimeraVezDeCobro()
        {
            var fuente = new FuenteCobroPrimerGiro();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Cliente", "Contacto", "Fecha", "Remesa", "Factura" },
                new object[] { "20000", "0", EVENTO.Date, 1234, "NV2600200" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(TemaEncuesta.AdministracionCobro, c[0].Tema);
            Assert.AreEqual(ClaseCandidataEncuesta.PrimeraVez, c[0].Clase);
            Assert.AreEqual("NV2600200", c[0].Factura);
        }

        // ------------------------------------------------------------ información técnica

        [TestMethod]
        public void Aparato_ALosDiasDelParametro_ConElProducto()
        {
            var fuente = new FuenteTecnicaAparato();
            var p = new ParametrosFuentesEncuesta { DiasTrasAparato = 20 };
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Pedido", "FechaEntrega", "Envio", "Cliente", "Contacto", "Producto", "NombreProducto" },
                new object[] { 930006, EVENTO, 251005, "21000", "0", "41000", "RADIOFRECUENCIA X" }), "1", fuente.RetrasoDias(p));

            Assert.AreEqual(20, fuente.RetrasoDias(p));
            Assert.AreEqual(15, fuente.RetrasoDias(new ParametrosFuentesEncuesta()));
            Assert.AreEqual(EVENTO.Date.AddDays(20), c[0].Dia);
            Assert.AreEqual("41000", c[0].Producto);
            Assert.AreEqual(TemaEncuesta.InformacionTecnica, c[0].Tema);
            StringAssert.Contains(c[0].Motivo, "aparato RADIOFRECUENCIA X (41000) el 20/11");
            Assert.IsNull(c[0].Profesora);
        }

        [TestMethod]
        public void FormacionSinEvento_LaReferenciaDeUnEventoNoCuenta()
        {
            var fuente = new FuenteTecnicaFormacionSinEvento();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "Pedido", "FechaFactura", "Factura", "Cliente", "Contacto", "Producto", "NombreProducto" },
                new object[] { 930007, EVENTO.Date, "CV2600001", "22000", "0", "90324", "FORMACIÓN" },
                new object[] { 930008, EVENTO.Date, "CV2600002", "22001", "0", "EVENTO00123", "MASTERCLASS" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(1, c.Count);
            Assert.AreEqual("90324", c[0].Producto);
            Assert.AreEqual(EVENTO.Date.AddDays(7), c[0].Dia);
        }

        [TestMethod]
        public void AsistenciaEvento_ConLaProfesoraYElEvento()
        {
            var fuente = new FuenteTecnicaAsistenciaEvento();
            List<CandidataEncuesta> c = fuente.Construir(Filas(new[] { "EventoId", "Fecha", "Titulo", "Ponente", "Producto", "Cliente", "Contacto" },
                new object[] { 45, EVENTO, "Masterclass Rosácea", "Lucía", "EVENTO00045", "23000", "0" },
                new object[] { 46, EVENTO, "Online", "", "", "23001", "0" }), "1", fuente.RetrasoDias(null));

            Assert.AreEqual(2, c.Count);
            Assert.AreEqual(45, c[0].EventoId);
            Assert.AreEqual("Lucía", c[0].Profesora);
            Assert.AreEqual("Asistió al curso «Masterclass Rosácea» el 20/11.", c[0].Motivo);
            Assert.IsNull(c[1].Profesora);
            Assert.IsNull(c[1].Producto);
        }

        [TestMethod]
        public void Todas_UnaFuentePorClaseYCadaTemaCubierto()
        {
            IReadOnlyList<IFuenteCandidatosEncuesta> todas = FuentesCandidatosEncuesta.Todas();

            Assert.AreEqual(todas.Count, todas.Select(f => f.Nombre).Distinct().Count());
            foreach (TemaEncuesta tema in Enum.GetValues(typeof(TemaEncuesta)))
            {
                Assert.IsTrue(todas.Any(f => f.Tema == tema), tema.ToString());
            }
            Assert.IsTrue(todas.All(f => f.Sql.Contains("READ UNCOMMITTED") && f.Sql.Contains("NOLOCK")));
        }

        [TestMethod]
        public void VentanaEvento_SeRestaElRetraso()
        {
            Tuple<DateTime, DateTime> v = LectorEncuestasSql.VentanaEvento(new DateTime(2026, 11, 26), new DateTime(2026, 11, 27), 2);

            Assert.AreEqual(new DateTime(2026, 11, 24), v.Item1);
            Assert.AreEqual(new DateTime(2026, 11, 25), v.Item2);
        }

        [DataTestMethod]
        [DataRow("15191", true)]
        [DataRow("31517", true)]
        [DataRow("32624", true)]
        [DataRow("10458", true)]
        [DataRow("9500", true)]
        [DataRow("15000", false)]
        public void ClientesQueNuncaSeEncuestan(string cliente, bool excluido)
        {
            Assert.AreEqual(excluido, ReglaClientesEncuesta.Excluido(cliente));
        }

        // ------------------------------------------------------------ canal y situación

        private static ContextoClientesEncuesta Contexto()
        {
            var ctx = new ContextoClientesEncuesta();
            _ = ctx.ClientesConApp.Add("1000");
            LectorEncuestasSql.AnadirCorreo(ctx, "1000", "0", "app@cliente.es");
            LectorEncuestasSql.AnadirCorreo(ctx, "2000", "1", "malo");
            LectorEncuestasSql.AnadirCorreo(ctx, "2000", "1", "bueno@cliente.es");
            LectorEncuestasSql.AnadirCorreo(ctx, "3000", "0", "otro@cliente.es");
            return ctx;
        }

        [TestMethod]
        public void Canal_ConAppPushAunqueTengaCorreo()
        {
            Assert.AreEqual(CanalesEncuesta.PUSH, ReglaCanalEncuesta.Canal(Contexto(), "1000", "0"));
        }

        [TestMethod]
        public void Canal_SinApp_CorreoValido()
        {
            ContextoClientesEncuesta ctx = Contexto();
            var c = new CandidataEncuesta { Cliente = "2000", Contacto = "1" };

            ReglaCanalEncuesta.Asignar(ctx, c);

            Assert.AreEqual(CanalesEncuesta.CORREO, c.Canal);
            Assert.AreEqual("bueno@cliente.es", c.Correo);
        }

        [TestMethod]
        public void Canal_OtroContactoSinCorreo_UsaElDeOtroContactoDelCliente()
        {
            Assert.AreEqual("otro@cliente.es", ReglaCanalEncuesta.Correo(Contexto(), "3000", "5"));
        }

        [TestMethod]
        public void Canal_SinNada_SinCanal()
        {
            Assert.AreEqual(CanalesEncuesta.SIN_CANAL, ReglaCanalEncuesta.Canal(Contexto(), "4000", "0"));
        }

        [TestMethod]
        public void Situacion_BajaDeTodoPorClienteOPorCorreo()
        {
            ContextoClientesEncuesta ctx = Contexto();
            ctx.Bajas = new BajasCorreosPostCompra();
            ctx.Bajas.Agregar("2000", null, null);
            ctx.Bajas.Agregar(null, "otro@cliente.es", null);
            ctx.Bajas.Agregar("1000", null, TiposCorreoCliente.VIDEOS_COMPRAS);

            Assert.IsTrue(ReglaCanalEncuesta.Situacion(ctx, "2000", EVENTO).DeBaja);
            Assert.IsTrue(ReglaCanalEncuesta.Situacion(ctx, "3000", EVENTO).DeBaja);
            Assert.IsFalse(ReglaCanalEncuesta.Situacion(ctx, "1000", EVENTO).DeBaja, "La baja de un tipo concreto no es baja de comunicaciones");
        }

        [TestMethod]
        public void Situacion_ImpagoSinLiquidarDesdeSuFecha()
        {
            ContextoClientesEncuesta ctx = Contexto();
            ctx.ImpagoPendienteDesde["2000"] = new DateTime(2026, 11, 10);

            Assert.IsFalse(ReglaCanalEncuesta.Situacion(ctx, "2000", new DateTime(2026, 11, 9)).ImpagoEnGestion);
            Assert.IsTrue(ReglaCanalEncuesta.Situacion(ctx, "2000", new DateTime(2026, 11, 10)).ImpagoEnGestion);
        }
    }
}
