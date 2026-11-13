using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos.DatosPagoAPlazos;

namespace NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: las reglas puras del pago a plazos de un pedido (disponibilidad, canal, plazos), el interruptor y el
    /// registro de proveedores. Todo con una financiera inventada: nada de esto sabe de Aplázame.
    /// </summary>
    [TestClass]
    public class ReglasPagoAPlazosTests
    {
        private static CondicionesProveedorPagoAPlazos Condiciones(decimal? minimo = 100M, decimal? maximo = 2500M, int dias = 15, bool operativo = true)
            => new CondicionesProveedorPagoAPlazos { Operativo = operativo, ImporteMinimo = minimo, ImporteMaximo = maximo, DiasPlazoCaptura = dias };

        private static string Motivo(PedidoPagoAPlazos pedido, ConfiguracionPagoAPlazos configuracion = null,
            CondicionesProveedorPagoAPlazos condiciones = null, params SolicitudPagoAPlazos[] solicitudes)
            => ReglasPagoAPlazos.MotivoNoDisponible(configuracion ?? Configuracion(), "Financiera Falsa", condiciones ?? Condiciones(),
                pedido, solicitudes, AHORA);

        #region Disponibilidad

        [TestMethod]
        public void PagoAPlazos_Disponible_PedidoPendienteDentroDeLosLimites()
        {
            Assert.IsNull(Motivo(Pedido()));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_InterruptorApagado()
        {
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, Motivo(Pedido(), Configuracion(ModoPagoAPlazos.Apagado)));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_ProveedorSinCondicionesONoOperativo()
        {
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE,
                ReglasPagoAPlazos.MotivoNoDisponible(Configuracion(), null, null, Pedido(), null, AHORA));
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, Motivo(Pedido(), condiciones: Condiciones(operativo: false)));
        }

        [TestMethod]
        public void PagoAPlazos_ModoPruebas_SoloLosClientesDePrueba()
        {
            ConfiguracionPagoAPlazos pruebas = Configuracion(ModoPagoAPlazos.Pruebas);
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, Motivo(Pedido(), pruebas));

            pruebas.ClientesPrueba = ConfiguracionPagoAPlazos.InterpretarClientes("1, 15191 ;2");
            Assert.IsNull(Motivo(Pedido(), pruebas));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_ImportePorDebajoDelMinimo()
        {
            string motivo = Motivo(Pedido(total: 60.50M));
            StringAssert.Contains(motivo, "desde 100,00 €");
            StringAssert.Contains(motivo, "60,50 €");
            StringAssert.Contains(motivo, "Financiera Falsa");
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_ImportePorEncimaDelMaximo()
        {
            StringAssert.Contains(Motivo(Pedido(total: 2500.01M)), "hasta 2.500,00 €");
        }

        [TestMethod]
        public void PagoAPlazos_Disponible_EnLosLimitesJustos()
        {
            Assert.IsNull(Motivo(Pedido(total: 100M)));
            Assert.IsNull(Motivo(Pedido(total: 2500M)));
        }

        [TestMethod]
        public void PagoAPlazos_SinLimites_CualquierImporte()
        {
            Assert.IsNull(Motivo(Pedido(total: 5M), condiciones: Condiciones(minimo: null, maximo: null)));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_PedidoYaPagadoConPrepagos()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Prepagos = 302.50M;
            Assert.AreEqual("El pedido ya está pagado.", Motivo(pedido));
        }

        [TestMethod]
        public void PagoAPlazos_ElImporteEsLoPendiente_DescontandoPrepagosYEnlacesCobrados()
        {
            PedidoPagoAPlazos pedido = Pedido(total: 500M);
            pedido.Prepagos = 100M;
            pedido.CobradoPorEnlaces = 50.25M;
            Assert.AreEqual(349.75M, ReglasPagoAPlazos.ImportePendiente(pedido));
        }

        [TestMethod]
        public void PagoAPlazos_ElImporte_NoCuentaPresupuestosNiLoFacturado()
        {
            PedidoPagoAPlazos pedido = Pedido(total: 200M);
            pedido.Lineas.Add(Linea(1000M, Constantes.EstadosLineaVenta.PRESUPUESTO));
            pedido.Lineas.Add(Linea(30M, Constantes.EstadosLineaVenta.ALBARAN));
            Assert.AreEqual(230M, ReglasPagoAPlazos.ImportePendiente(pedido));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_PedidoFacturado()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(200M, Constantes.EstadosLineaVenta.FACTURA));
            StringAssert.Contains(Motivo(pedido), "ya está facturado");
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_Presupuesto()
        {
            StringAssert.Contains(Motivo(Pedido(estado: Constantes.EstadosLineaVenta.PRESUPUESTO)), "presupuesto");
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_NotaDeEntrega()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.EsNotaEntrega = true;
            StringAssert.Contains(Motivo(pedido), "notas de entrega");
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_YaFinanciado()
        {
            StringAssert.Contains(Motivo(Pedido(), null, null, Solicitud(EstadosPagoAPlazos.APROBADO)), "ya está financiado con Financiera Falsa");
            StringAssert.Contains(Motivo(Pedido(), null, null, Solicitud(EstadosPagoAPlazos.CAPTURADO)), "ya está financiado");
        }

        [TestMethod]
        public void PagoAPlazos_Disponible_ConUnaSolicitudPendienteOCerrada()
        {
            Assert.IsNull(Motivo(Pedido(), null, null, Solicitud(EstadosPagoAPlazos.PENDIENTE), Solicitud(EstadosPagoAPlazos.RECHAZADO, id: 8),
                Solicitud(EstadosPagoAPlazos.CADUCADO, id: 9), Solicitud(EstadosPagoAPlazos.CANCELADO, id: 10)));
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_LineaQueNoPuedeSalirEnElPlazoDeCaptura()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, entrega: AHORA.Date.AddDays(16)));
            string motivo = Motivo(pedido);
            StringAssert.Contains(motivo, "15 días");
            StringAssert.Contains(motivo, AHORA.Date.AddDays(16).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            StringAssert.Contains(motivo, "no puede salir en plazo");
        }

        [TestMethod]
        public void PagoAPlazos_Disponible_LineaConEntregaElUltimoDiaDelPlazo()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, entrega: AHORA.Date.AddDays(15)));
            Assert.IsNull(Motivo(pedido));
        }

        [TestMethod]
        public void PagoAPlazos_ElPlazoEsUnDatoDelProveedor()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, entrega: AHORA.Date.AddDays(12)));
            Assert.IsNull(Motivo(pedido, condiciones: Condiciones(dias: 15)));
            StringAssert.Contains(Motivo(pedido, condiciones: Condiciones(dias: 10)), "10 días");
        }

        [TestMethod]
        public void PagoAPlazos_NoDisponible_LineaSinFechaDeEntrega()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, entrega: new DateTime(2099, 12, 31)));
            StringAssert.Contains(Motivo(pedido), "sin fecha de entrega");
        }

        [TestMethod]
        public void PagoAPlazos_LaFechaDeUnaLineaYaServida_NoImpide()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, Constantes.EstadosLineaVenta.ALBARAN, AHORA.Date.AddDays(90)));
            Assert.IsNull(Motivo(pedido));
        }

        #endregion

        #region Canal

        [TestMethod]
        public void PagoAPlazos_Canal_SinPedirlo_AppSiElClienteLaTieneYEnlaceSiNo()
        {
            Assert.AreEqual(CanalesPagoAPlazos.APP, ReglasPagoAPlazos.ElegirCanal(null, true, out _, out _));
            Assert.AreEqual(CanalesPagoAPlazos.ENLACE, ReglasPagoAPlazos.ElegirCanal("  ", false, out _, out _));
        }

        [TestMethod]
        public void PagoAPlazos_Canal_EnlaceSiempreVale()
        {
            Assert.AreEqual(CanalesPagoAPlazos.ENLACE, ReglasPagoAPlazos.ElegirCanal("enlace", true, out string motivo, out bool conflicto));
            Assert.IsNull(motivo);
            Assert.IsFalse(conflicto);
        }

        [TestMethod]
        public void PagoAPlazos_Canal_AppSinApp_EsUnConflicto()
        {
            Assert.IsNull(ReglasPagoAPlazos.ElegirCanal("App", false, out string motivo, out bool conflicto));
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_SIN_APP, motivo);
            Assert.IsTrue(conflicto);
        }

        [TestMethod]
        public void PagoAPlazos_Canal_Desconocido_NoVale()
        {
            Assert.IsNull(ReglasPagoAPlazos.ElegirCanal("Correo", true, out string motivo, out bool conflicto));
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_CANAL_NO_VALIDO, motivo);
            Assert.IsFalse(conflicto);
        }

        #endregion

        #region Plazos y estados

        private static SolicitudPagoAPlazos Aprobada(int diasHastaElLimite, bool avisada = false)
        {
            SolicitudPagoAPlazos solicitud = Solicitud(EstadosPagoAPlazos.APROBADO);
            solicitud.FechaAprobacion = AHORA.AddDays(diasHastaElLimite - 15);
            solicitud.FechaLimiteCaptura = AHORA.AddDays(diasHastaElLimite);
            solicitud.FechaAvisoPlazo = avisada ? AHORA.AddDays(-1) : (DateTime?)null;
            return solicitud;
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_PedidoServidoEntero_Capturar()
        {
            Assert.AreEqual(AccionPlazoPagoAPlazos.Capturar,
                ReglasPagoAPlazos.DecidirPlazo(Aprobada(10), Pedido(estado: Constantes.EstadosLineaVenta.ALBARAN), AHORA, 3));
            Assert.AreEqual(AccionPlazoPagoAPlazos.Capturar,
                ReglasPagoAPlazos.DecidirPlazo(Aprobada(-2), Pedido(estado: Constantes.EstadosLineaVenta.FACTURA), AHORA, 3));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_AMedioServir_TodaviaNoSeCaptura()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, Constantes.EstadosLineaVenta.ALBARAN));
            Assert.AreEqual(AccionPlazoPagoAPlazos.Nada, ReglasPagoAPlazos.DecidirPlazo(Aprobada(10), pedido, AHORA, 3));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_QuedanPocosDiasSinSalir_AvisarUnaVez()
        {
            Assert.AreEqual(AccionPlazoPagoAPlazos.Nada, ReglasPagoAPlazos.DecidirPlazo(Aprobada(4), Pedido(), AHORA, 3));
            Assert.AreEqual(AccionPlazoPagoAPlazos.Avisar, ReglasPagoAPlazos.DecidirPlazo(Aprobada(3), Pedido(), AHORA, 3));
            Assert.AreEqual(AccionPlazoPagoAPlazos.Nada, ReglasPagoAPlazos.DecidirPlazo(Aprobada(3, avisada: true), Pedido(), AHORA, 3));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_Vencido_SinSalirNada_Caducar()
        {
            Assert.AreEqual(AccionPlazoPagoAPlazos.Caducar, ReglasPagoAPlazos.DecidirPlazo(Aprobada(0), Pedido(), AHORA, 3));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_Vencido_ConParteServida_SeCobraLoServido()
        {
            PedidoPagoAPlazos pedido = Pedido();
            pedido.Lineas.Add(Linea(50M, Constantes.EstadosLineaVenta.ALBARAN));
            Assert.AreEqual(AccionPlazoPagoAPlazos.CapturarLoServido, ReglasPagoAPlazos.DecidirPlazo(Aprobada(-1), pedido, AHORA, 3));
            Assert.AreEqual(50M, ReglasPagoAPlazos.ImporteServido(pedido));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_PedidoBorradoOSinLineas_Cancelar()
        {
            Assert.AreEqual(AccionPlazoPagoAPlazos.CancelarSinPedido, ReglasPagoAPlazos.DecidirPlazo(Aprobada(10), null, AHORA, 3));
            PedidoPagoAPlazos vacio = Pedido();
            vacio.Lineas.Clear();
            Assert.AreEqual(AccionPlazoPagoAPlazos.CancelarSinPedido, ReglasPagoAPlazos.DecidirPlazo(Aprobada(10), vacio, AHORA, 3));
        }

        [TestMethod]
        public void PagoAPlazos_Plazo_SoloParaLasAprobadas()
        {
            foreach (string estado in new[] { EstadosPagoAPlazos.PENDIENTE, EstadosPagoAPlazos.CAPTURADO, EstadosPagoAPlazos.CANCELADO,
                EstadosPagoAPlazos.CADUCADO, EstadosPagoAPlazos.RECHAZADO })
            {
                Assert.AreEqual(AccionPlazoPagoAPlazos.Nada, ReglasPagoAPlazos.DecidirPlazo(Solicitud(estado), null, AHORA, 3), estado);
            }
        }

        [TestMethod]
        public void PagoAPlazos_PendienteCaduca_AlPasarLosDiasDeValidez()
        {
            SolicitudPagoAPlazos solicitud = Solicitud();
            solicitud.FechaCreacion = AHORA.AddDays(-7);
            Assert.IsTrue(ReglasPagoAPlazos.PendienteCaducada(solicitud, AHORA, 7));
            Assert.IsFalse(ReglasPagoAPlazos.PendienteCaducada(solicitud, AHORA, 8));
            solicitud.Estado = EstadosPagoAPlazos.APROBADO;
            Assert.IsFalse(ReglasPagoAPlazos.PendienteCaducada(solicitud, AHORA, 7));
        }

        [TestMethod]
        public void PagoAPlazos_DiasHasta_HaciaArribaYNuncaNegativo()
        {
            Assert.AreEqual(3, ReglasPagoAPlazos.DiasHasta(AHORA.AddDays(2.2), AHORA));
            Assert.AreEqual(1, ReglasPagoAPlazos.DiasHasta(AHORA.AddHours(1), AHORA));
            Assert.AreEqual(0, ReglasPagoAPlazos.DiasHasta(AHORA.AddHours(-1), AHORA));
        }

        [TestMethod]
        public void PagoAPlazos_LaQueManda_LaConcedidaAntesQueLaUltima()
        {
            SolicitudPagoAPlazos aprobada = Solicitud(EstadosPagoAPlazos.APROBADO, id: 3);
            SolicitudPagoAPlazos cancelada = Solicitud(EstadosPagoAPlazos.CANCELADO, id: 5);
            Assert.AreSame(aprobada, ReglasPagoAPlazos.LaQueManda(new[] { aprobada, cancelada }));
            Assert.AreSame(cancelada, ReglasPagoAPlazos.LaQueManda(new[] { Solicitud(EstadosPagoAPlazos.RECHAZADO, id: 4), cancelada }));
            Assert.IsNull(ReglasPagoAPlazos.LaQueManda(null));
        }

        [TestMethod]
        public void PagoAPlazos_ReferenciaYConcepto_EstablesYDe50Caracteres()
        {
            Assert.AreEqual("PAP-123", ReglasPagoAPlazos.Referencia(123));
            Assert.AreEqual("Falsa pedido 930500 PAP-123", ReglasPagoAPlazos.ConceptoPrepago("Falsa pedido", 930500, "PAP-123"));
            Assert.AreEqual(50, ReglasPagoAPlazos.ConceptoPrepago(new string('x', 60), 930500, "PAP-123").Length);
        }

        #endregion

        #region Interruptor

        [TestMethod]
        public void PagoAPlazos_Interruptor_SinFilaOValorRaro_Apagado()
        {
            Assert.AreEqual(ModoPagoAPlazos.Apagado, ConfiguracionPagoAPlazos.InterpretarModo(null));
            Assert.AreEqual(ModoPagoAPlazos.Apagado, ConfiguracionPagoAPlazos.InterpretarModo(""));
            Assert.AreEqual(ModoPagoAPlazos.Apagado, ConfiguracionPagoAPlazos.InterpretarModo("1"));
            Assert.AreEqual(ModoPagoAPlazos.Activo, ConfiguracionPagoAPlazos.InterpretarModo(" activo "));
            Assert.AreEqual(ModoPagoAPlazos.Pruebas, ConfiguracionPagoAPlazos.InterpretarModo("Pruebas"));
            Assert.AreEqual(ModoPagoAPlazos.Pruebas, ConfiguracionPagoAPlazos.InterpretarModo("Sandbox"));
        }

        [TestMethod]
        public void PagoAPlazos_Interruptor_SeLeeDeDefecto_YEsIndependienteDelDeLaApp()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", "PagoAPlazosPedidos")).Returns("Activo");
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", "PagoAPlazosProveedor")).Returns(" Aplazame ");
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", "PagoAPlazosDiasAviso")).Returns("5");
            // El de la app, apagado: no pinta nada aquí
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", Constantes.ParametrosUsuario.APLAZAME_APP)).Returns("Apagado");

            ConfiguracionPagoAPlazos configuracion = ConfiguracionPagoAPlazos.Leer(lector);

            Assert.AreEqual(ModoPagoAPlazos.Activo, configuracion.Modo);
            Assert.AreEqual("Aplazame", configuracion.Proveedor);
            Assert.AreEqual(5, configuracion.DiasAviso);
            Assert.AreEqual(ConfiguracionPagoAPlazos.DIAS_VALIDEZ_POR_DEFECTO, configuracion.DiasValidezSolicitud);
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.APLAZAME_APP)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PagoAPlazos_Interruptor_SiNoSePuedeLeer_Apagado()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("sin base de datos"));

            ConfiguracionPagoAPlazos configuracion = ConfiguracionPagoAPlazos.Leer(lector);

            Assert.AreEqual(ModoPagoAPlazos.Apagado, configuracion.Modo);
            Assert.IsFalse(configuracion.SeOfreceA(CLIENTE));
            Assert.AreEqual(ConfiguracionPagoAPlazos.DIAS_AVISO_POR_DEFECTO, configuracion.DiasAviso);
        }

        [TestMethod]
        public void PagoAPlazos_Dias_FueraDeRango_ElValorPorDefecto()
        {
            Assert.AreEqual(3, ConfiguracionPagoAPlazos.InterpretarDias("0", 3));
            Assert.AreEqual(3, ConfiguracionPagoAPlazos.InterpretarDias("abc", 3));
            Assert.AreEqual(3, ConfiguracionPagoAPlazos.InterpretarDias("61", 3));
            Assert.AreEqual(60, ConfiguracionPagoAPlazos.InterpretarDias("60", 3));
        }

        #endregion

        #region Registro de proveedores

        [TestMethod]
        public void PagoAPlazos_Registro_ResuelvePorCodigoSinMirarMayusculas()
        {
            FinancieraFalsa falsa = new FinancieraFalsa();
            IProveedorPagoAPlazos otra = A.Fake<IProveedorPagoAPlazos>();
            A.CallTo(() => otra.Codigo).Returns("Otra");
            RegistroProveedoresPagoAPlazos registro = new RegistroProveedoresPagoAPlazos(falsa, otra);

            Assert.AreSame(falsa, registro.PorCodigo("falsa"));
            Assert.AreSame(falsa, registro.PorCodigo(" FALSA "));
            Assert.AreSame(otra, registro.PorCodigo("Otra"));
            CollectionAssert.AreEquivalent(new[] { "Falsa", "Otra" }, registro.Codigos.ToList());
        }

        [TestMethod]
        public void PagoAPlazos_Registro_CodigoDesconocidoOVacio_Null()
        {
            RegistroProveedoresPagoAPlazos registro = new RegistroProveedoresPagoAPlazos(new FinancieraFalsa());
            Assert.IsNull(registro.PorCodigo("Klarna"));
            Assert.IsNull(registro.PorCodigo(null));
            Assert.IsNull(registro.PorCodigo(" "));
        }

        [TestMethod]
        public void PagoAPlazos_Registro_DosConElMismoCodigo_NoSeAdmite()
        {
            _ = Assert.ThrowsException<ArgumentException>(() => new RegistroProveedoresPagoAPlazos(new FinancieraFalsa(), new FinancieraFalsa()));
        }

        [TestMethod]
        public void PagoAPlazos_Registro_ElDeVerdad_TieneAAplazameYElCodigoDelScript()
        {
            IRegistroProveedoresPagoAPlazos registro = RegistroProveedoresPagoAPlazos.PorDefecto();
            IProveedorPagoAPlazos aplazame = registro.PorCodigo("Aplazame");
            Assert.IsNotNull(aplazame);
            Assert.AreEqual("Aplázame", aplazame.Nombre);
        }

        #endregion

        #region Avisos: el contrato con las apps

        [TestMethod]
        public void PagoAPlazos_PushAlCliente_TipoYDatosParaAbrirElPedido_YDeTu()
        {
            NotificacionPushDTO push = TextosAvisosPagoAPlazos.AlCliente(Solicitud(), "Financiera Falsa");

            Assert.AreEqual("PagoAPlazosPendiente", push.Tipo);
            Assert.AreEqual("PagoAPlazosPendiente", push.Datos["tipo"]);
            Assert.AreEqual("1", push.Datos["empresa"]);
            Assert.AreEqual("930500", push.Datos["pedido"]);
            Assert.AreEqual("7", push.Datos["idSolicitud"]);
            Assert.AreEqual("302.50", push.Datos["importe"]);
            Assert.AreEqual("Financiera Falsa", push.Datos["proveedor"]);
            Assert.AreEqual("Pendiente", push.Datos["estado"]);
            Assert.AreEqual("Tienes un pedido pendiente de pago", push.Titulo);
            StringAssert.Contains(push.Cuerpo, "tu pedido 930500");
            StringAssert.Contains(push.Cuerpo, "302,50 €");
            StringAssert.Contains(push.Cuerpo, "Financiera Falsa");
            Assert.IsFalse(push.Cuerpo.Contains("usted") || push.Cuerpo.Contains(" su "), "La app de clientes habla de tú");
        }

        [TestMethod]
        public void PagoAPlazos_PushAprobadoAlCliente_TipoPropioMismosDatosQueElPendiente_YDeTu()
        {
            NotificacionPushDTO push = TextosAvisosPagoAPlazos.AprobadoAlCliente(Solicitud(EstadosPagoAPlazos.APROBADO), "Financiera Falsa");

            Assert.AreEqual("PagoAPlazosAprobadoCliente", push.Tipo);
            Assert.AreEqual("PagoAPlazosAprobadoCliente", push.Datos["tipo"]);
            Assert.AreEqual("Aprobado", push.Datos["estado"]);
            CollectionAssert.AreEquivalent(TextosAvisosPagoAPlazos.AlCliente(Solicitud(), "Financiera Falsa").Datos.Keys.ToList(), push.Datos.Keys.ToList(),
                "Los mismos Datos que la push del pendiente: la app los lee igual");
            Assert.AreEqual("1", push.Datos["empresa"]);
            Assert.AreEqual("930500", push.Datos["pedido"]);
            Assert.AreEqual("7", push.Datos["idSolicitud"]);
            Assert.AreEqual("15191", push.Datos["cliente"]);
            Assert.AreEqual("Financiera Falsa", push.Datos["proveedor"]);
            Assert.AreEqual("302.50", push.Datos["importe"], "Con punto decimal");
            Assert.AreEqual("Pago a plazos aprobado", push.Titulo);
            Assert.AreEqual("Tu pago a plazos del pedido 930500 (302,50 €) con Financiera Falsa está aprobado. Te avisaremos cuando salga.", push.Cuerpo);
        }

        [TestMethod]
        public void PagoAPlazos_PushRechazadoAlCliente_TipoPropioYLeMandaASuVendedor()
        {
            NotificacionPushDTO push = TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), "Financiera Falsa", false);

            Assert.AreEqual("PagoAPlazosRechazadoCliente", push.Tipo);
            Assert.AreEqual("PagoAPlazosRechazadoCliente", push.Datos["tipo"]);
            Assert.AreEqual("Rechazado", push.Datos["estado"]);
            Assert.AreEqual("930500", push.Datos["pedido"]);
            Assert.AreEqual("302.50", push.Datos["importe"]);
            Assert.AreEqual("Pago a plazos no aprobado", push.Titulo);
            Assert.AreEqual("Financiera Falsa no ha aprobado el pago a plazos del pedido 930500. Habla con tu vendedor para ver otra forma de pago.", push.Cuerpo);

            NotificacionPushDTO despues = TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), "Financiera Falsa", true);
            Assert.AreEqual("PagoAPlazosRechazadoCliente", despues.Tipo);
            Assert.AreEqual("Financiera Falsa ha anulado el pago a plazos del pedido 930500 que había aprobado. Habla con tu vendedor para ver otra forma de pago.",
                despues.Cuerpo);
        }

        [TestMethod]
        public void PagoAPlazos_PushesDelResultadoAlCliente_DeTuNeutrasYSinDatosDelVendedor()
        {
            foreach (NotificacionPushDTO push in new[]
            {
                TextosAvisosPagoAPlazos.AprobadoAlCliente(Solicitud(EstadosPagoAPlazos.APROBADO), "Financiera Falsa"),
                TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), "Financiera Falsa", false),
                TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), "Financiera Falsa", true),
                TextosAvisosPagoAPlazos.AprobadoAlCliente(Solicitud(EstadosPagoAPlazos.APROBADO), null),
                TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), " ", false)
            })
            {
                string texto = push.Titulo + " " + push.Cuerpo;
                Assert.IsFalse(texto.Contains("usted") || texto.Contains(" su ") || texto.Contains("Hable"), "La app de clientes habla de tú: " + texto);
                Assert.IsFalse(texto.IndexOf("plazame", StringComparison.OrdinalIgnoreCase) >= 0, "El nombre de la financiera lo da el proveedor: " + texto);
                Assert.IsFalse(texto.Contains("cliente 15191") || texto.Contains("prepago") || texto.Contains("Paloma"), "Nada de lo del vendedor: " + texto);
                Assert.IsFalse(texto.Contains("el proveedor"), "Al cliente no se le habla de «el proveedor»: " + texto);
            }
            Assert.AreEqual("La financiera no ha aprobado el pago a plazos del pedido 930500. Habla con tu vendedor para ver otra forma de pago.",
                TextosAvisosPagoAPlazos.RechazadoAlCliente(Solicitud(EstadosPagoAPlazos.RECHAZADO), null, false).Cuerpo);
            StringAssert.Contains(TextosAvisosPagoAPlazos.AprobadoAlCliente(Solicitud(EstadosPagoAPlazos.APROBADO), null).Cuerpo, "con la financiera está aprobado");
        }

        [TestMethod]
        public void PagoAPlazos_AvisosDelResultadoAlCliente_VanALaAppDeClientesDeEseClienteYDevuelvenLosDispositivos()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            List<NotificacionPushDTO> enviadas = new List<NotificacionPushDTO>();
            _ = A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>._))
                .Invokes((string e, string c, NotificacionPushDTO n) => enviadas.Add(n))
                .Returns(2);
            AvisosPagoAPlazos avisos = new AvisosPagoAPlazos(() => push, (e, p) => throw new InvalidOperationException("Al cliente no se le buscan comerciales"));
            SolicitudPagoAPlazos solicitud = Solicitud(EstadosPagoAPlazos.APROBADO);
            solicitud.Cliente = "15191     ";
            solicitud.Empresa = "1  ";

            Assert.AreEqual(2, avisos.AprobadoAlCliente(solicitud, "Financiera Falsa").Result);
            Assert.AreEqual(2, avisos.RechazadoAlCliente(solicitud, "Financiera Falsa", false).Result);

            CollectionAssert.AreEqual(new[] { "PagoAPlazosAprobadoCliente", "PagoAPlazosRechazadoCliente" }, enviadas.Select(n => n.Tipo).ToList());
            // Solo por EnviarACliente (la app de clientes: sin dispositivo ni manda ni guarda en el buzón); nada a Nesto ni a NestoApp
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarAUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarAVendedor(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();

            solicitud.Cliente = " ";
            Assert.AreEqual(0, avisos.AprobadoAlCliente(solicitud, "Financiera Falsa").Result, "Sin cliente, nada");
            Assert.AreEqual(2, enviadas.Count);
        }

        [TestMethod]
        public void PagoAPlazos_AvisoAprobado_LlevaElPedidoElLimiteYLoQueFalta()
        {
            SolicitudPagoAPlazos solicitud = Solicitud(EstadosPagoAPlazos.APROBADO);
            solicitud.FechaLimiteCaptura = new DateTime(2026, 11, 28, 12, 0, 0);

            NotificacionPushDTO aviso = TextosAvisosPagoAPlazos.Aprobado(solicitud, "Financiera Falsa", 25.5M, contraReembolso: true);

            Assert.AreEqual("PagoAPlazosAprobado", aviso.Tipo);
            Assert.AreEqual("930500", aviso.Datos["pedido"]);
            Assert.AreEqual("Aprobado", aviso.Datos["estado"]);
            Assert.AreEqual("2026-11-28T12:00:00", aviso.Datos["fechaLimite"]);
            StringAssert.Contains(aviso.Cuerpo, "antes del 28/11/2026");
            StringAssert.Contains(aviso.Cuerpo, "25,50 €");
            StringAssert.Contains(aviso.Cuerpo, "contra reembolso");
        }

        [TestMethod]
        public void PagoAPlazos_AvisoPlazoCerca_DiceLosDiasYElPedido()
        {
            SolicitudPagoAPlazos solicitud = Solicitud(EstadosPagoAPlazos.APROBADO);
            solicitud.FechaLimiteCaptura = new DateTime(2026, 11, 16, 12, 0, 0);

            NotificacionPushDTO aviso = TextosAvisosPagoAPlazos.PlazoCerca(solicitud, "Financiera Falsa", 3);

            Assert.AreEqual("PagoAPlazosPlazoCerca", aviso.Tipo);
            Assert.AreEqual("3", aviso.Datos["dias"]);
            Assert.AreEqual("930500", aviso.Datos["pedido"]);
            StringAssert.Contains(aviso.Cuerpo, "Quedan 3 días para enviar el pedido 930500");
            StringAssert.Contains(aviso.Cuerpo, "se pierde la financiación de Financiera Falsa");
            StringAssert.Contains(TextosAvisosPagoAPlazos.PlazoCerca(solicitud, "Financiera Falsa", 1).Cuerpo, "Queda 1 día");
        }

        [TestMethod]
        public void PagoAPlazos_AvisosRechazadoYPerdido_SusTiposYEstados()
        {
            Assert.AreEqual("PagoAPlazosRechazado", TextosAvisosPagoAPlazos.Rechazado(Solicitud(), "Financiera Falsa").Tipo);
            NotificacionPushDTO perdido = TextosAvisosPagoAPlazos.Perdido(Solicitud(EstadosPagoAPlazos.CADUCADO), "Financiera Falsa", "El pedido no ha salido en plazo.");
            Assert.AreEqual("PagoAPlazosPerdido", perdido.Tipo);
            Assert.AreEqual("Caducado", perdido.Datos["estado"]);
            StringAssert.Contains(perdido.Cuerpo, "El pedido no ha salido en plazo.");
            Assert.AreEqual("Cancelado", TextosAvisosPagoAPlazos.Perdido(Solicitud(EstadosPagoAPlazos.CANCELADO), "X", "m").Datos["estado"]);
        }

        [TestMethod]
        public void PagoAPlazos_Destinatarios_QuienHizoElPedido_SuVendedor_YQuienPidioLaFinanciacion()
        {
            ComercialesPedido comerciales = new ComercialesPedido
            {
                Empresa = "1",
                Pedido = PEDIDO,
                UsuarioPedido = "Paloma",
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "Jesus" } } }
            };

            DestinatariosAvisoPagoAPlazos mismos = TextosAvisosPagoAPlazos.Destinatarios(comerciales, "NUEVAVISION\\Paloma");
            DestinatariosAvisoPagoAPlazos conOtro = TextosAvisosPagoAPlazos.Destinatarios(comerciales, "NUEVAVISION\\Laura");
            DestinatariosAvisoPagoAPlazos vendedorApp = TextosAvisosPagoAPlazos.Destinatarios(comerciales, "carlos");

            CollectionAssert.Contains(mismos.UsuariosNestoApp, "Paloma");
            Assert.AreEqual(1, mismos.UsuariosNestoApp.Count, "Quien pidió la financiación es quien hizo el pedido: un solo aviso");
            CollectionAssert.Contains(conOtro.BuzonNesto, "NUEVAVISION\\Laura");
            CollectionAssert.Contains(conOtro.UsuariosNestoApp, "Laura");
            CollectionAssert.Contains(vendedorApp.UsuariosNestoApp, "carlos");
            Assert.IsFalse(vendedorApp.BuzonNesto.Any(u => u.EndsWith("carlos")), "Sin dominio no tiene campana de Nesto");
        }

        [TestMethod]
        public void PagoAPlazos_Destinatarios_SinPedido_SoloQuienPidioLaFinanciacion()
        {
            DestinatariosAvisoPagoAPlazos destinatarios = TextosAvisosPagoAPlazos.Destinatarios(null, "NUEVAVISION\\Paloma");
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Paloma" }, destinatarios.BuzonNesto);
            CollectionAssert.AreEqual(new[] { "Paloma" }, destinatarios.UsuariosNestoApp);
        }

        #endregion

        [TestMethod]
        public void PagoAPlazos_Sql_LasColumnasQueSeLeenSonLasPropiedadesDeLaFila()
        {
            // SqlQuery<T> exige una columna por propiedad: si se añade una a la clase y no a la consulta (o al revés), falla en producción
            List<string> columnas = RepositorioPagoAPlazos.COLUMNAS.Split(',').Select(c => c.Trim()).OrderBy(c => c).ToList();
            List<string> propiedades = typeof(SolicitudPagoAPlazos).GetProperties().Select(p => p.Name).OrderBy(p => p).ToList();
            CollectionAssert.AreEqual(propiedades, columnas);
        }

        [TestMethod]
        public void PagoAPlazos_Script_CreaLaTablaConEsasColumnasYLosParametrosQueLeeLaApi()
        {
            string ruta = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Scripts", "Issue864_PagoAPlazos.sql"));
            if (!System.IO.File.Exists(ruta))
            {
                Assert.Inconclusive("No se encuentra el script desde " + AppDomain.CurrentDomain.BaseDirectory);
            }
            string script = System.IO.File.ReadAllText(ruta);

            StringAssert.Contains(script, "CREATE TABLE dbo." + RepositorioPagoAPlazos.TABLA);
            StringAssert.Contains(script, "OBJECT_ID(N'dbo." + RepositorioPagoAPlazos.TABLA + "'");
            foreach (string columna in RepositorioPagoAPlazos.COLUMNAS.Split(',').Select(c => c.Trim()))
            {
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(script, @"\n\s+" + columna + @"\s"), "Falta la columna " + columna);
            }
            foreach (string clave in new[] { ConfiguracionPagoAPlazos.CLAVE_INTERRUPTOR, ConfiguracionPagoAPlazos.CLAVE_CLIENTES_PRUEBA,
                ConfiguracionPagoAPlazos.CLAVE_PROVEEDOR, ConfiguracionPagoAPlazos.CLAVE_DIAS_AVISO, ConfiguracionPagoAPlazos.CLAVE_DIAS_VALIDEZ })
            {
                StringAssert.Contains(script, $"'{clave}'");
            }
            foreach (string estado in new[] { EstadosPagoAPlazos.PENDIENTE, EstadosPagoAPlazos.APROBADO, EstadosPagoAPlazos.CAPTURADO,
                EstadosPagoAPlazos.CANCELADO, EstadosPagoAPlazos.CADUCADO, EstadosPagoAPlazos.RECHAZADO })
            {
                StringAssert.Contains(script, $"'{estado}'");
            }
            // El interruptor nace apagado y el proveedor por defecto es el que está dado de alta
            StringAssert.Contains(script, "'PagoAPlazosPedidos', 'Apagado'");
            StringAssert.Contains(script, "'PagoAPlazosProveedor', '" + NestoAPI.Infraestructure.Pagos.Aplazame.ProveedorPagoAPlazosAplazame.CODIGO + "'");
        }

        [TestMethod]
        public void PagoAPlazos_Sql_CompatibleConSqlServer2008()
        {
            // BD en nivel de compatibilidad 100: nada de funciones posteriores
            string[] prohibido = { "TRY_CONVERT", "IIF(", "CONCAT(", "FORMAT(", "STRING_AGG", "OFFSET ", "FETCH ", "LAG(", "LEAD(", "THROW" };
            string[] sentencias =
            {
                RepositorioPagoAPlazos.SQL_LEER, RepositorioPagoAPlazos.SQL_LEER_POR_REFERENCIA, RepositorioPagoAPlazos.SQL_LEER_DEL_PEDIDO,
                RepositorioPagoAPlazos.SQL_LEER_VIVAS, RepositorioPagoAPlazos.SQL_CREAR, RepositorioPagoAPlazos.SQL_REFERENCIAS,
                RepositorioPagoAPlazos.SQL_CAPTURA, RepositorioPagoAPlazos.SQL_AVISO, RepositorioPagoAPlazos.SQL_PREPAGO,
                RepositorioPagoAPlazos.SqlCambiarEstado(2)
            };
            foreach (string sql in sentencias)
            {
                foreach (string palabra in prohibido)
                {
                    Assert.IsFalse(sql.ToUpperInvariant().Contains(palabra), $"«{palabra}» en: {sql}");
                }
            }
            StringAssert.Contains(RepositorioPagoAPlazos.SqlCambiarEstado(2), "Estado IN (@desde0, @desde1)");
        }

        // ---- Carlos 13/11/26: invitar al cliente a instalar la app

        [TestMethod]
        public void PagoAPlazos_InvitacionApp_SoloSiNoLaTiene_YLlevaElEnlaceDeInstalacion()
        {
            Assert.IsNull(ReglasPagoAPlazos.InvitacionApp(clienteTieneApp: true));
            string invitacion = ReglasPagoAPlazos.InvitacionApp(clienteTieneApp: false);
            StringAssert.EndsWith(invitacion, ReglasPagoAPlazos.ENLACE_APP);
            Assert.IsFalse(invitacion.ToLowerInvariant().Contains("aplazame") || invitacion.ToLowerInvariant().Contains("aplázame"),
                "Neutro: la invitación no nombra a ninguna financiera");
        }

        [TestMethod]
        public void PagoAPlazos_MensajeParaElCliente_ElEnlaceYLaInvitacionSoloSinApp()
        {
            Assert.IsNull(ReglasPagoAPlazos.MensajeParaElCliente(null, 930500, "Financiera", false), "Sin enlace no hay mensaje");
            Assert.IsNull(ReglasPagoAPlazos.MensajeParaElCliente(" ", 930500, "Financiera", false));

            string conApp = ReglasPagoAPlazos.MensajeParaElCliente("https://pago.example/1", 930500, "Financiera", true);
            Assert.AreEqual("Aquí tienes el enlace para pagar a plazos tu pedido 930500 con Financiera: https://pago.example/1", conApp);

            string sinApp = ReglasPagoAPlazos.MensajeParaElCliente("https://pago.example/1", 930500, null, false);
            StringAssert.StartsWith(sinApp, "Aquí tienes el enlace para pagar a plazos tu pedido 930500 con la financiera: https://pago.example/1");
            StringAssert.EndsWith(sinApp, ReglasPagoAPlazos.ENLACE_APP);
        }
    }
}
