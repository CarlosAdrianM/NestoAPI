using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos.DatosPagoAPlazos;

namespace NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: el núcleo del pago a plazos de un pedido, con una financiera INVENTADA (<see cref="FinancieraFalsa"/>): si
    /// todo esto funciona sin una sola mención a Aplázame, es que cambiar de financiera es implementar la interfaz. Lo que
    /// importa por encima de todo: que el pedido no tenga prepago sin la aprobación del proveedor por el importe exacto, que
    /// no se apunte dos veces, que no se cobre dos veces y que lo que no sale a tiempo se cancele.
    /// </summary>
    [TestClass]
    public class ServicioPagoAPlazosTests
    {
        private RepositorioPagoAPlazosEnMemoria repositorio;
        private PedidosPagoAPlazosEnMemoria pedidos;
        private FinancieraFalsa financiera;
        private AvisosPagoAPlazosEspia avisos;
        private ConfiguracionPagoAPlazos configuracion;
        private List<Exception> errores;
        private DateTime ahora;
        private ServicioPagoAPlazos servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioPagoAPlazosEnMemoria();
            pedidos = new PedidosPagoAPlazosEnMemoria();
            pedidos.Pedidos[PEDIDO] = Pedido();
            financiera = new FinancieraFalsa();
            avisos = new AvisosPagoAPlazosEspia();
            configuracion = Configuracion();
            errores = new List<Exception>();
            ahora = AHORA;
            servicio = new ServicioPagoAPlazos(repositorio, pedidos, new RegistroProveedoresPagoAPlazos(financiera), () => configuracion, avisos)
            {
                RegistrarError = e => errores.Add(e),
                Ahora = () => ahora
            };
        }

        private Task<RespuestaNotificacionProveedor> Notificar(string referencia, TipoNotificacionPagoAPlazos tipo, decimal? importe = 302.50M,
            string secreto = FinancieraFalsa.SECRETO)
            => servicio.ProcesarNotificacion(FinancieraFalsa.CODIGO, secreto,
                $"{referencia}|{tipo}" + (importe.HasValue ? "|" + importe.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty));

        private SolicitudPagoAPlazos Fila(int id = 7) => repositorio.Filas.Single(f => f.Id == id);

        private SolicitudPagoAPlazos Aprobada(int diasHastaElLimite = 10)
        {
            SolicitudPagoAPlazos solicitud = Solicitud(EstadosPagoAPlazos.APROBADO);
            solicitud.FechaAprobacion = AHORA.AddDays(diasHastaElLimite - 10);
            solicitud.FechaLimiteCaptura = AHORA.AddDays(diasHastaElLimite);
            solicitud.PrepagoId = 101;
            pedidos.Prepagos.Add(new PrepagoEnMemoria { Id = 101, Pedido = PEDIDO, Importe = solicitud.Importe, Concepto = "Falsa pedido 930500 PAP-7" });
            return repositorio.Anadir(solicitud);
        }

        #region Disponible

        [TestMethod]
        public async Task PagoAPlazos_Disponible_DiceElProveedorSusLimitesYElCanal()
        {
            ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO> resultado = await servicio.Disponible("1", PEDIDO);

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            DisponibilidadPagoAPlazosDTO dto = resultado.Contenido;
            Assert.IsTrue(dto.Disponible);
            Assert.IsNull(dto.Motivo);
            Assert.AreEqual("Falsa", dto.Proveedor);
            Assert.AreEqual("Financiera Falsa", dto.NombreProveedor);
            Assert.AreEqual(302.50M, dto.Importe);
            Assert.AreEqual(50M, dto.ImporteMinimo);
            Assert.AreEqual(3000M, dto.ImporteMaximo);
            Assert.AreEqual(10, dto.DiasPlazoCaptura);
            Assert.IsTrue(dto.ClienteTieneApp);
            Assert.AreEqual("App", dto.Canal);
            Assert.IsNull(dto.Aviso);
        }

        [TestMethod]
        public async Task PagoAPlazos_Disponible_ClienteSinApp_SugiereEnlaceYLoDice()
        {
            pedidos.Pedidos[PEDIDO] = Pedido(tieneApp: false);

            DisponibilidadPagoAPlazosDTO dto = (await servicio.Disponible("1", PEDIDO)).Contenido;

            Assert.IsTrue(dto.Disponible);
            Assert.IsFalse(dto.ClienteTieneApp);
            Assert.AreEqual("Enlace", dto.Canal);
            Assert.AreEqual(ReglasPagoAPlazos.AVISO_SIN_APP, dto.Aviso);
        }

        [TestMethod]
        public async Task PagoAPlazos_Disponible_InterruptorApagado_200ConElMotivoYSinDecirElProveedor()
        {
            configuracion.Modo = ModoPagoAPlazos.Apagado;

            ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO> resultado = await servicio.Disponible("1", PEDIDO);

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            Assert.IsFalse(resultado.Contenido.Disponible);
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, resultado.Contenido.Motivo);
            Assert.IsNull(resultado.Contenido.Proveedor);
            Assert.IsNull(resultado.Contenido.ImporteMinimo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Disponible_ProveedorDelParametroSinDarDeAlta_NoSeOfreceYSeRegistra()
        {
            configuracion.Proveedor = "Klarna";

            DisponibilidadPagoAPlazosDTO dto = (await servicio.Disponible("1", PEDIDO)).Contenido;

            Assert.IsFalse(dto.Disponible);
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, dto.Motivo);
            Assert.IsTrue(errores.Any(e => e.Message.Contains("Klarna")));
        }

        [TestMethod]
        public async Task PagoAPlazos_Disponible_FueraDeLosLimitesDelProveedor_MotivoConSusLimites()
        {
            financiera.Minimo = 500M;

            DisponibilidadPagoAPlazosDTO dto = (await servicio.Disponible("1", PEDIDO)).Contenido;

            Assert.IsFalse(dto.Disponible);
            StringAssert.Contains(dto.Motivo, "500,00 €");
            Assert.AreEqual(500M, dto.ImporteMinimo);
            Assert.AreEqual("Falsa", dto.Proveedor);
        }

        [TestMethod]
        public async Task PagoAPlazos_Disponible_PedidoQueNoExiste_404()
        {
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Disponible("1", 1)).Codigo);
        }

        [TestMethod]
        public async Task PagoAPlazos_SinElScript_503ConElNombreDelScript_EnTodo()
        {
            repositorio.HayTabla = false;

            ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO> disponible = await servicio.Disponible("1", PEDIDO);
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> solicitar = await servicio.Solicitar("1", PEDIDO, null, "Paloma");
            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> estado = await servicio.Estado("1", PEDIDO);
            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> cancelar = await servicio.Cancelar("1", PEDIDO, null, "Paloma");

            foreach (HttpStatusCode codigo in new[] { disponible.Codigo, solicitar.Codigo, estado.Codigo, cancelar.Codigo })
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, codigo);
            }
            StringAssert.Contains(disponible.Mensaje, "Scripts/Issue864_PagoAPlazos.sql");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.ParaElCliente("1", CLIENTE, PEDIDO)).Codigo);
            Assert.AreEqual(0, financiera.Creadas.Count);
        }

        #endregion

        #region Solicitar

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_CanalApp_CreaLaSolicitudEnElProveedorYMandaLaPush()
        {
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "App" }, "NUEVAVISION\\Paloma");

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            SolicitudPagoAPlazos fila = repositorio.Filas.Single();
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, fila.Estado);
            Assert.AreEqual("Falsa", fila.Proveedor);
            Assert.AreEqual("PAP-" + fila.Id, fila.Referencia);
            Assert.AreEqual("EXT-PAP-" + fila.Id, fila.ReferenciaExterna);
            Assert.AreEqual(302.50M, fila.Importe);
            Assert.AreEqual("App", fila.Canal);
            Assert.AreEqual("NUEVAVISION\\Paloma", fila.Usuario);

            PeticionSolicitudProveedor peticion = financiera.Creadas.Single();
            Assert.AreEqual(fila.Referencia, peticion.Referencia);
            Assert.AreEqual(302.50M, peticion.Importe);
            Assert.AreEqual("cliente@example.com", peticion.Correo);
            Assert.AreEqual("https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Falsa", peticion.UrlNotificacion);
            Assert.AreEqual(1, peticion.Lineas.Count);

            SolicitudPagoAPlazosDTO dto = resultado.Contenido;
            Assert.AreEqual("Pendiente", dto.Estado);
            Assert.AreEqual("App", dto.Canal);
            Assert.AreEqual("Financiera Falsa", dto.NombreProveedor);
            Assert.AreEqual(1, dto.DispositivosAvisados);
            Assert.IsNull(dto.UrlPago, "Por la app no hace falta el enlace");
            CollectionAssert.AreEqual(new[] { $"Cliente:{PEDIDO}:Financiera Falsa" }, avisos.Avisos);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_CanalEnlace_DevuelveLaUrlYNoMandaPush()
        {
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "Enlace" }, "Paloma");

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            Assert.AreEqual("Enlace", resultado.Contenido.Canal);
            Assert.AreEqual("https://falsa.example/Enlace/EXT-PAP-1", resultado.Contenido.UrlPago);
            Assert.AreEqual(0, avisos.Avisos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_SinCanal_AppSiLaTieneYEnlaceSiNo()
        {
            Assert.AreEqual("App", (await servicio.Solicitar("1", PEDIDO, null, "Paloma")).Contenido.Canal);

            Preparar();
            pedidos.Pedidos[PEDIDO] = Pedido(tieneApp: false);
            SolicitudPagoAPlazosDTO sinApp = (await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO(), "Paloma")).Contenido;
            Assert.AreEqual("Enlace", sinApp.Canal);
            Assert.IsNotNull(sinApp.UrlPago);
            Assert.AreEqual(ReglasPagoAPlazos.AVISO_SIN_APP, sinApp.Aviso);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_AppSinQueElClienteLaTenga_409YNoSeCreaNada()
        {
            pedidos.Pedidos[PEDIDO] = Pedido(tieneApp: false);

            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "App" }, "Paloma");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.Codigo);
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_SIN_APP, resultado.Mensaje);
            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(0, financiera.Creadas.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_CanalDesconocido_400()
        {
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "Fax" }, "Paloma");
            Assert.AreEqual(HttpStatusCode.BadRequest, resultado.Codigo);
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_NoDisponible_409ConElMotivo()
        {
            configuracion.Modo = ModoPagoAPlazos.Apagado;
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.Codigo);
            Assert.AreEqual(ReglasPagoAPlazos.MOTIVO_NO_DISPONIBLE, resultado.Mensaje);
            Assert.AreEqual(0, financiera.Creadas.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_PedidoQueNoExiste_404()
        {
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Solicitar("1", 1, null, "Paloma")).Codigo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_SinCorreoDelCliente_400_YConElDeLaPeticionSiVale()
        {
            pedidos.Pedidos[PEDIDO].Correo = null;

            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> sinCorreo = await servicio.Solicitar("1", PEDIDO, null, "Paloma");
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> conCorreo = await servicio.Solicitar("1", PEDIDO,
                new PeticionPagoAPlazosDTO { Correo = " otro@example.com " }, "Paloma");

            Assert.AreEqual(HttpStatusCode.BadRequest, sinCorreo.Codigo);
            StringAssert.Contains(sinCorreo.Mensaje, "correo");
            Assert.AreEqual(HttpStatusCode.OK, conCorreo.Codigo);
            Assert.AreEqual("otro@example.com", financiera.Creadas.Single().Correo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_ElProveedorFalla_502YLaSolicitudQuedaCancelada()
        {
            financiera.FallaAlCrear = true;

            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Codigo);
            StringAssert.Contains(resultado.Mensaje, "Financiera Falsa");
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, repositorio.Filas.Single().Estado);
            Assert.AreEqual(0, avisos.Avisos.Count);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_DosVecesLoMismo_EsLaMismaSolicitudYSeVuelveAAvisar()
        {
            _ = await servicio.Solicitar("1", PEDIDO, null, "Paloma");
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> segunda = await servicio.Solicitar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.OK, segunda.Codigo);
            Assert.AreEqual(1, repositorio.Filas.Count);
            Assert.AreEqual(1, financiera.Creadas.Count, "No se abre otra solicitud en el proveedor");
            Assert.AreEqual(2, avisos.Avisos.Count, "La push sí se repite");
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_CambiandoDeCanal_LaAnteriorSeCierraYSeAbreOtra()
        {
            _ = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "App" }, "Paloma");
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> enlace = await servicio.Solicitar("1", PEDIDO, new PeticionPagoAPlazosDTO { Canal = "Enlace" }, "Paloma");

            Assert.AreEqual(HttpStatusCode.OK, enlace.Codigo);
            Assert.AreEqual(2, repositorio.Filas.Count);
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila(1).Estado);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, Fila(2).Estado);
            Assert.AreEqual(2, enlace.Contenido.IdSolicitud);

            // Y si el proveedor aprobara la primera, ya no vale
            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-1", TipoNotificacionPagoAPlazos.PideConfirmacion);
            Assert.AreEqual("Rechazar", respuesta.Cuerpo);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_YaAprobada_409()
        {
            _ = Aprobada();
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.Codigo);
            Assert.AreEqual(0, financiera.Creadas.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_LaPushNoSale_DevuelveElEnlaceYLoDice()
        {
            avisos.Dispositivos = 0;
            SolicitudPagoAPlazosDTO dto = (await servicio.Solicitar("1", PEDIDO, null, "Paloma")).Contenido;

            Assert.AreEqual("App", dto.Canal);
            Assert.AreEqual(0, dto.DispositivosAvisados);
            Assert.AreEqual("https://falsa.example/Enlace/EXT-PAP-1", dto.UrlPago);
            StringAssert.Contains(dto.Aviso, "mándele el enlace");
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_LaPushFalla_NoRompeLaSolicitud()
        {
            avisos.FallaLaPush = true;
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> resultado = await servicio.Solicitar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, repositorio.Filas.Single().Estado);
            Assert.IsNotNull(resultado.Contenido.UrlPago);
        }

        [TestMethod]
        public async Task PagoAPlazos_Solicitar_ModoPruebas_LaSolicitudVaAlEntornoDePruebas()
        {
            configuracion.Modo = ModoPagoAPlazos.Pruebas;
            configuracion.ClientesPrueba = new HashSet<string> { CLIENTE };

            SolicitudPagoAPlazosDTO dto = (await servicio.Solicitar("1", PEDIDO, null, "Paloma")).Contenido;

            Assert.IsTrue(dto.Pruebas);
            Assert.IsTrue(financiera.Creadas.Single().Pruebas);
            Assert.IsTrue(repositorio.Filas.Single().Pruebas);
        }

        #endregion

        #region Aprobación: el prepago

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ApuntaElPrepagoConLaFechaDelCobroYAvisa()
        {
            _ = repositorio.Anadir(Solicitud());

            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual("Aceptar", respuesta.Cuerpo);
            Assert.AreEqual(299, respuesta.CodigoHttp, "El código y el cuerpo los pone el proveedor");
            SolicitudPagoAPlazos fila = Fila();
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, fila.Estado);
            Assert.AreEqual(AHORA, fila.FechaAprobacion);
            Assert.AreEqual(AHORA.AddDays(10), fila.FechaLimiteCaptura, "El plazo de captura es el del proveedor");

            PrepagoEnMemoria prepago = pedidos.Prepagos.Single();
            Assert.AreEqual(PEDIDO, prepago.Pedido);
            Assert.AreEqual(302.50M, prepago.Importe);
            Assert.AreEqual("57299999", prepago.Cuenta, "La cuenta es la del proveedor");
            Assert.AreEqual("Falsa pedido 930500 PAP-7", prepago.Concepto);
            Assert.AreEqual(AHORA.Date, prepago.FechaCobro, "NestoAPI#860: la fecha real del cobro");
            Assert.AreEqual(prepago.Id, fila.PrepagoId);
            CollectionAssert.AreEqual(new[] { $"Aprobado:{PEDIDO}:0:False" }, avisos.Avisos);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_Repetida_NiDuplicaElPrepagoNiVuelveAAvisar()
        {
            _ = repositorio.Anadir(Solicitud());

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);
            RespuestaNotificacionProveedor segunda = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);
            RespuestaNotificacionProveedor final = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada);

            Assert.AreEqual("Aceptar", segunda.Cuerpo);
            Assert.AreEqual("Aceptar", final.Cuerpo);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
            Assert.AreEqual(1, avisos.Avisos.Count);
            Assert.AreEqual(AHORA, Fila().FechaAprobacion);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElPrepagoFalla_SubeElErrorYElReintentoLoApunta()
        {
            _ = repositorio.Anadir(Solicitud());
            pedidos.FallaElPrepago = true;

            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion));
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
            Assert.AreEqual(0, pedidos.Prepagos.Count);

            pedidos.FallaElPrepago = false;
            RespuestaNotificacionProveedor reintento = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual("Aceptar", reintento.Cuerpo);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
            Assert.AreEqual(AHORA.Date, pedidos.Prepagos.Single().FechaCobro);
            Assert.AreEqual(1, avisos.Avisos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ImporteDistinto_NoSeConfirmaNiSeApuntaNada()
        {
            _ = repositorio.Anadir(Solicitud());

            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion, 1M);

            Assert.AreEqual("Rechazar", respuesta.Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.RECHAZADO, Fila().Estado);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
            CollectionAssert.AreEqual(new[] { $"Rechazado:{PEDIDO}" }, avisos.Avisos);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_SinImporte_NoSeConfirma()
        {
            _ = repositorio.Anadir(Solicitud());
            Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion, null)).Cuerpo);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_AutorizacionQueNoEsLaSuya_NoTocaNada()
        {
            _ = repositorio.Anadir(Solicitud());

            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion, secreto: "otra cosa");

            Assert.AreEqual("NoAutorizada", respuesta.Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, Fila().Estado);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Notificacion_IlegibleReferenciaDesconocidaYProveedorQueNoExiste()
        {
            Assert.AreEqual("Ilegible", (await servicio.ProcesarNotificacion("Falsa", FinancieraFalsa.SECRETO, "basura")).Cuerpo);
            Assert.AreEqual("Desconocida", (await Notificar("PAP-999", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
            Assert.AreEqual(404, (await servicio.ProcesarNotificacion("Klarna", FinancieraFalsa.SECRETO, "x|Aprobada")).CodigoHttp);
        }

        [TestMethod]
        public async Task PagoAPlazos_Notificacion_SinLaTabla_QueElProveedorReintente()
        {
            repositorio.HayTabla = false;
            Assert.AreEqual("Reintentar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_DeUnaSolicitudDeOtroProveedor_NoLaEncuentra()
        {
            SolicitudPagoAPlazos deOtro = Solicitud();
            deOtro.Proveedor = "Aplazame";
            _ = repositorio.Anadir(deOtro);

            Assert.AreEqual("Desconocida", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, Fila().Estado);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_DeUnaCanceladaCaducadaORechazada_NoSeConfirma()
        {
            foreach (string estado in new[] { EstadosPagoAPlazos.CANCELADO, EstadosPagoAPlazos.CADUCADO, EstadosPagoAPlazos.RECHAZADO })
            {
                Preparar();
                _ = repositorio.Anadir(Solicitud(estado));

                Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo, estado);
                Assert.AreEqual(estado, Fila().Estado);
                Assert.AreEqual(0, pedidos.Prepagos.Count);
            }
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElPedidoYaNoExiste_NoSeConfirma()
        {
            _ = repositorio.Anadir(Solicitud());
            _ = pedidos.Pedidos.Remove(PEDIDO);

            Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila().Estado);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElPedidoYaDebeMenosDeLoQueSePidio_NoSeConfirma()
        {
            _ = repositorio.Anadir(Solicitud());
            pedidos.Pedidos[PEDIDO] = Pedido(total: 200M);

            Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila().Estado);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
            Assert.IsTrue(avisos.Avisos.Single().StartsWith($"Perdido:{PEDIDO}:El pedido ha cambiado"));
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElPedidoHaCrecido_SeApruebaYElAvisoDiceLoQueFalta()
        {
            _ = repositorio.Anadir(Solicitud());
            pedidos.Pedidos[PEDIDO] = Pedido(total: 352.50M);
            pedidos.Pedidos[PEDIDO].ContraReembolso = true;

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
            Assert.AreEqual(302.50M, pedidos.Prepagos.Single().Importe);
            CollectionAssert.AreEqual(new[] { $"Aprobado:{PEDIDO}:50:True" }, avisos.Avisos);
        }

        [TestMethod]
        public async Task PagoAPlazos_FinalAprobada_SinHaberConfirmado_TambienApuntaElPrepago()
        {
            _ = repositorio.Anadir(Solicitud());

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_FinalAprobada_DeUnaCancelada_SoloSeRegistra()
        {
            _ = repositorio.Anadir(Solicitud(EstadosPagoAPlazos.CANCELADO));

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada)).Cuerpo);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
            Assert.IsTrue(errores.Any(e => e.Message.Contains("Cancelado")));
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_DeUnaPendiente_QuedaRechazadaYAvisa()
        {
            _ = repositorio.Anadir(Solicitud());

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.RECHAZADO, Fila().Estado);
            Assert.IsNotNull(Fila().FechaCierre);
            CollectionAssert.AreEqual(new[] { $"Rechazado:{PEDIDO}" }, avisos.Avisos);

            // Repetida: nada más
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);
            Assert.AreEqual(1, avisos.Avisos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_DespuesDeAprobar_QuitaElPrepagoYAvisa()
        {
            _ = Aprobada();

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);

            Assert.AreEqual(EstadosPagoAPlazos.RECHAZADO, Fila().Estado);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
            Assert.IsTrue(avisos.Avisos.Single().StartsWith($"Perdido:{PEDIDO}:"));
        }

        [TestMethod]
        public async Task PagoAPlazos_PasoIntermedio_NoCambiaNada()
        {
            _ = repositorio.Anadir(Solicitud());

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Intermedia)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, Fila().Estado);
            Assert.AreEqual(0, avisos.AvisosCliente.Count, "«En revisión» no es ni aprobado ni rechazado: al cliente, nada todavía");
        }

        #endregion

        #region El resultado, también al cliente

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_AvisaTambienAlCliente_YLaRepetidaNoRepiteLaPush()
        {
            _ = repositorio.Anadir(Solicitud());

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            CollectionAssert.AreEqual(new[] { $"AprobadoCliente:{PEDIDO}:Financiera Falsa:Aprobado" }, avisos.AvisosCliente);

            // El proveedor repite la confirmación y luego manda la final: ni otro prepago ni otra push
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada);

            Assert.AreEqual(1, avisos.AvisosCliente.Count);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_TrasEstarEnRevision_AvisaAlClienteAlAprobarse()
        {
            _ = repositorio.Anadir(Solicitud());

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Intermedia);
            Assert.AreEqual(0, avisos.AvisosCliente.Count);

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada);

            CollectionAssert.AreEqual(new[] { $"AprobadoCliente:{PEDIDO}:Financiera Falsa:Aprobado" }, avisos.AvisosCliente);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_PorEnlace_TambienAvisaAlClienteSiTieneLaApp()
        {
            _ = repositorio.Anadir(Solicitud(canal: CanalesPagoAPlazos.ENLACE));

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual(1, avisos.AvisosCliente.Count, "Los dispositivos los mira el aviso, no el canal por el que se le mandó");
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_LaPushAlClienteFalla_NoRompeLaAprobacionNiLaRespuestaAlProveedor()
        {
            _ = repositorio.Anadir(Solicitud());
            avisos.FallaLaPush = true;

            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual("Aceptar", respuesta.Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
            CollectionAssert.AreEqual(new[] { $"Aprobado:{PEDIDO}:0:False" }, avisos.Avisos, "El del vendedor sale igual");
            Assert.IsTrue(errores.Single().Message.Contains("al cliente de la aprobación"), "A ELMAH, como los demás avisos");
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElAvisoAlVendedorFalla_ElClienteSeEnteraIgual()
        {
            _ = repositorio.Anadir(Solicitud());
            avisos.FallaElAvisoAlVendedor = true;

            RespuestaNotificacionProveedor respuesta = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual("Aceptar", respuesta.Cuerpo);
            Assert.AreEqual(1, avisos.AvisosCliente.Count);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_ElPrepagoFalla_ElClienteNoSeEnteraHastaElReintento_YUnaSolaVez()
        {
            _ = repositorio.Anadir(Solicitud());
            pedidos.FallaElPrepago = true;

            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion));
            Assert.AreEqual(0, avisos.AvisosCliente.Count, "Sin prepago todavía no es verdad que esté todo hecho");

            pedidos.FallaElPrepago = false;
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Aprobada);

            Assert.AreEqual(1, avisos.AvisosCliente.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Aprobacion_QueNoSeConfirma_NoDiceAlClienteQueEstaAprobado()
        {
            _ = repositorio.Anadir(Solicitud());
            pedidos.Pedidos[PEDIDO] = Pedido(total: 200M);

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion);

            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila().Estado);
            Assert.AreEqual(0, avisos.AvisosCliente.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_AvisaTambienAlCliente_YElRepetidoNoRepiteLaPush()
        {
            _ = repositorio.Anadir(Solicitud());

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);

            CollectionAssert.AreEqual(new[] { $"RechazadoCliente:{PEDIDO}:Financiera Falsa:Rechazado:False" }, avisos.AvisosCliente);

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);
            Assert.AreEqual(1, avisos.AvisosCliente.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_LaPushAlClienteFalla_NoRompeElRechazo()
        {
            _ = repositorio.Anadir(Solicitud());
            avisos.FallaLaPush = true;

            Assert.AreEqual("Aceptar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada)).Cuerpo);
            Assert.AreEqual(EstadosPagoAPlazos.RECHAZADO, Fila().Estado);
            CollectionAssert.AreEqual(new[] { $"Rechazado:{PEDIDO}" }, avisos.Avisos);
            Assert.IsTrue(errores.Single().Message.Contains("al cliente del rechazo"));
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_PorImporteDistinto_TambienSeLeDiceAlCliente()
        {
            _ = repositorio.Anadir(Solicitud());

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion, 1M);
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion, 1M);

            CollectionAssert.AreEqual(new[] { $"RechazadoCliente:{PEDIDO}:Financiera Falsa:Rechazado:False" }, avisos.AvisosCliente);
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_DespuesDeAprobar_SeLeDiceAlClienteQueYaNo()
        {
            _ = Aprobada();

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);
            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);

            CollectionAssert.AreEqual(new[] { $"RechazadoCliente:{PEDIDO}:Financiera Falsa:Rechazado:True" }, avisos.AvisosCliente);
        }

        [TestMethod]
        public async Task PagoAPlazos_Rechazo_DeUnaYaCerrada_NoAvisaAlCliente()
        {
            _ = repositorio.Anadir(Solicitud(EstadosPagoAPlazos.CANCELADO));

            _ = await Notificar("PAP-7", TipoNotificacionPagoAPlazos.Rechazada);

            Assert.AreEqual(0, avisos.AvisosCliente.Count);
        }

        #endregion

        #region Estado

        [TestMethod]
        public async Task PagoAPlazos_Estado_SinSolicitud_200ConSinSolicitud_Y404SiNoHayPedido()
        {
            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> sin = await servicio.Estado("1", PEDIDO);
            Assert.AreEqual(HttpStatusCode.OK, sin.Codigo);
            Assert.IsFalse(sin.Contenido.HaySolicitud);
            Assert.AreEqual("SinSolicitud", sin.Contenido.Estado);

            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Estado("1", 1)).Codigo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Estado_PendientePorEnlace_LlevaLaUrl()
        {
            _ = repositorio.Anadir(Solicitud(canal: CanalesPagoAPlazos.ENLACE));

            EstadoPagoAPlazosDTO dto = (await servicio.Estado("1", PEDIDO)).Contenido;

            Assert.AreEqual("Pendiente", dto.Estado);
            Assert.AreEqual("https://falsa.example/Enlace/EXT-PAP-7", dto.UrlPago);
            Assert.AreEqual(302.50M, dto.Importe);
            Assert.AreEqual("Financiera Falsa", dto.NombreProveedor);
            Assert.IsNull(dto.DiasParaCapturar);
        }

        [TestMethod]
        public async Task PagoAPlazos_Estado_Aprobado_DiceElLimiteYLosDiasQueQuedan()
        {
            _ = Aprobada(diasHastaElLimite: 4);

            EstadoPagoAPlazosDTO dto = (await servicio.Estado("1", PEDIDO)).Contenido;

            Assert.AreEqual("Aprobado", dto.Estado);
            Assert.AreEqual(AHORA.AddDays(4), dto.FechaLimiteCaptura);
            Assert.AreEqual(4, dto.DiasParaCapturar);
            Assert.IsNull(dto.UrlPago);
        }

        [TestMethod]
        public async Task PagoAPlazos_Estado_CadaEstadoGuardadoSaleTalCual()
        {
            foreach (string estado in new[] { EstadosPagoAPlazos.PENDIENTE, EstadosPagoAPlazos.APROBADO, EstadosPagoAPlazos.CAPTURADO,
                EstadosPagoAPlazos.CANCELADO, EstadosPagoAPlazos.CADUCADO, EstadosPagoAPlazos.RECHAZADO })
            {
                Preparar();
                _ = repositorio.Anadir(Solicitud(estado));
                Assert.AreEqual(estado, (await servicio.Estado("1", PEDIDO)).Contenido.Estado);
            }
        }

        [TestMethod]
        public async Task PagoAPlazos_Estado_ConUnProveedorQueYaNoEstaDadoDeAlta_SigueContestando()
        {
            SolicitudPagoAPlazos antigua = Solicitud(EstadosPagoAPlazos.CAPTURADO);
            antigua.Proveedor = "Antigua";
            _ = repositorio.Anadir(antigua);

            EstadoPagoAPlazosDTO dto = (await servicio.Estado("1", PEDIDO)).Contenido;

            Assert.AreEqual("Capturado", dto.Estado);
            Assert.AreEqual("Antigua", dto.NombreProveedor);
        }

        [TestMethod]
        public async Task PagoAPlazos_ParaElCliente_SoloSuPedido_YConLaUrlDeLaApp()
        {
            _ = repositorio.Anadir(Solicitud());

            ResultadoPagoAPlazos<PagoAPlazosClienteDTO> suyo = await servicio.ParaElCliente("1", CLIENTE, PEDIDO);
            ResultadoPagoAPlazos<PagoAPlazosClienteDTO> deOtro = await servicio.ParaElCliente("1", "99999", PEDIDO);
            ResultadoPagoAPlazos<PagoAPlazosClienteDTO> sinSolicitud = await servicio.ParaElCliente("1", CLIENTE, 123);

            Assert.AreEqual(HttpStatusCode.OK, suyo.Codigo);
            Assert.AreEqual("https://falsa.example/App/EXT-PAP-7", suyo.Contenido.UrlPago);
            Assert.AreEqual("Pendiente", suyo.Contenido.Estado);
            Assert.AreEqual(302.50M, suyo.Contenido.Importe);
            Assert.AreEqual(HttpStatusCode.NotFound, deOtro.Codigo);
            Assert.AreEqual(HttpStatusCode.NotFound, sinSolicitud.Codigo);
            Assert.AreEqual(deOtro.Mensaje, sinSolicitud.Mensaje, "La misma respuesta: no se dice qué hay ni de quién");
        }

        [TestMethod]
        public async Task PagoAPlazos_ParaElCliente_YaAprobada_SinUrl()
        {
            _ = Aprobada();
            PagoAPlazosClienteDTO dto = (await servicio.ParaElCliente("1", CLIENTE, PEDIDO)).Contenido;
            Assert.AreEqual("Aprobado", dto.Estado);
            Assert.IsNull(dto.UrlPago);
        }

        #endregion

        #region Cancelar

        [TestMethod]
        public async Task PagoAPlazos_Cancelar_Pendiente_SoloAquiYYaNoSePuedeAprobar()
        {
            _ = repositorio.Anadir(Solicitud());

            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> resultado = await servicio.Cancelar("1", PEDIDO, "El cliente prefiere tarjeta", "NUEVAVISION\\Paloma");

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            Assert.AreEqual("Cancelado", resultado.Contenido.Estado);
            StringAssert.Contains(resultado.Contenido.Motivo, "El cliente prefiere tarjeta");
            Assert.AreEqual(0, financiera.Canceladas.Count, "El proveedor no había aprobado nada");
            Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Cancelar_Aprobada_CancelaEnElProveedorYQuitaElPrepago()
        {
            _ = Aprobada();

            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> resultado = await servicio.Cancelar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.OK, resultado.Codigo);
            Assert.AreEqual("Cancelado", resultado.Contenido.Estado);
            CollectionAssert.AreEqual(new[] { "PAP-7" }, financiera.Canceladas);
            Assert.AreEqual(0, pedidos.Prepagos.Count);
            Assert.IsTrue(avisos.Avisos.Single().StartsWith($"Perdido:{PEDIDO}:"));
        }

        [TestMethod]
        public async Task PagoAPlazos_Cancelar_ElProveedorNoDeja_502YNoSeTocaNada()
        {
            _ = Aprobada();
            financiera.FallaAlCancelar = true;

            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> resultado = await servicio.Cancelar("1", PEDIDO, null, "Paloma");

            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Codigo);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Cancelar_SinNadaVivo_409_YSinPedido_404()
        {
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Cancelar("1", PEDIDO, null, "Paloma")).Codigo);
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Cancelar("1", 1, null, "Paloma")).Codigo);

            _ = repositorio.Anadir(Solicitud(EstadosPagoAPlazos.CAPTURADO));
            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> cobrada = await servicio.Cancelar("1", PEDIDO, null, "Paloma");
            Assert.AreEqual(HttpStatusCode.Conflict, cobrada.Codigo);
            StringAssert.Contains(cobrada.Mensaje, "ya está cobrado");
        }

        [TestMethod]
        public async Task PagoAPlazos_PedidoBorrado_CancelaLoVivo_YNuncaLanza()
        {
            _ = Aprobada();
            _ = pedidos.Pedidos.Remove(PEDIDO);

            await servicio.CancelarPorPedidoBorrado("1", PEDIDO, "NUEVAVISION\\Paloma");

            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila().Estado);
            CollectionAssert.AreEqual(new[] { "PAP-7" }, financiera.Canceladas);

            Preparar();
            _ = Aprobada();
            financiera.FallaAlCancelar = true;
            await servicio.CancelarPorPedidoBorrado("1", PEDIDO, "Paloma");
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado, "Lo reintentará el job");
            Assert.IsTrue(errores.Any(e => e.Message.Contains("NO se ha podido cancelar")));

            Preparar();
            repositorio.HayTabla = false;
            await servicio.CancelarPorPedidoBorrado("1", PEDIDO, "Paloma");
        }

        #endregion

        #region Job

        [TestMethod]
        public async Task PagoAPlazos_Job_PedidoServido_ConfirmaElCobroUnaSolaVez()
        {
            _ = Aprobada();
            pedidos.Pedidos[PEDIDO] = Pedido(estado: Constantes.EstadosLineaVenta.ALBARAN);

            ResultadoPlazosPagoAPlazos primera = await servicio.ProcesarPlazos(ahora);
            ResultadoPlazosPagoAPlazos segunda = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, primera.Capturadas);
            Assert.AreEqual(0, segunda.Capturadas.Count);
            KeyValuePair<string, decimal> captura = financiera.Capturas.Single();
            Assert.AreEqual("PAP-7", captura.Key);
            Assert.AreEqual(302.50M, captura.Value);
            Assert.AreEqual(EstadosPagoAPlazos.CAPTURADO, Fila().Estado);
            Assert.AreEqual(302.50M, Fila().ImporteCapturado);
            Assert.AreEqual(AHORA, Fila().FechaCaptura);
            Assert.AreEqual(1, pedidos.Prepagos.Count, "El prepago sigue: es el cobro del pedido");
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_YaCobradaEnElProveedor_SeApuntaSinVolverACobrar()
        {
            _ = Aprobada();
            pedidos.Pedidos[PEDIDO] = Pedido(estado: Constantes.EstadosLineaVenta.FACTURA);
            financiera.YaCapturado = new EstadoCapturaProveedor { Capturado = 302.50M, PendienteDeCapturar = 0, FechaUltimaCaptura = AHORA.AddHours(-3) };

            _ = await servicio.ProcesarPlazos(ahora);

            Assert.AreEqual(0, financiera.Capturas.Count);
            Assert.AreEqual(EstadosPagoAPlazos.CAPTURADO, Fila().Estado);
            Assert.AreEqual(AHORA.AddHours(-3), Fila().FechaCaptura);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PedidoQueSalePorMenos_CobraLoServido()
        {
            _ = Aprobada();
            pedidos.Pedidos[PEDIDO] = Pedido(total: 250M, estado: Constantes.EstadosLineaVenta.ALBARAN);

            _ = await servicio.ProcesarPlazos(ahora);

            Assert.AreEqual(250M, financiera.Capturas.Single().Value);
            Assert.IsTrue(errores.Any(e => e.Message.Contains("250")));
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_NuncaCobraMasDeLoAprobado()
        {
            _ = Aprobada();
            pedidos.Pedidos[PEDIDO] = Pedido(total: 900M, estado: Constantes.EstadosLineaVenta.ALBARAN);

            _ = await servicio.ProcesarPlazos(ahora);

            Assert.AreEqual(302.50M, financiera.Capturas.Single().Value);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_SinSalirYLejosDelLimite_Nada()
        {
            _ = Aprobada(diasHastaElLimite: 9);

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            Assert.AreEqual(0, resultado.Capturadas.Count + resultado.Avisadas.Count + resultado.Caducadas.Count + resultado.Canceladas.Count);
            Assert.AreEqual(0, avisos.Avisos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_QuedanPocosDias_AvisaUnaSolaVez()
        {
            _ = Aprobada(diasHastaElLimite: 3);

            ResultadoPlazosPagoAPlazos primera = await servicio.ProcesarPlazos(ahora);
            ResultadoPlazosPagoAPlazos segunda = await servicio.ProcesarPlazos(ahora.AddHours(1));

            CollectionAssert.AreEqual(new[] { 7 }, primera.Avisadas);
            Assert.AreEqual(0, segunda.Avisadas.Count);
            CollectionAssert.AreEqual(new[] { $"PlazoCerca:{PEDIDO}:3" }, avisos.Avisos);
            Assert.AreEqual(AHORA, Fila().FechaAvisoPlazo);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_LosDiasDeAvisoSonUnParametro()
        {
            _ = Aprobada(diasHastaElLimite: 5);
            configuracion.DiasAviso = 5;

            Assert.AreEqual(1, (await servicio.ProcesarPlazos(ahora)).Avisadas.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PlazoVencidoSinSalir_CancelaEnElProveedorQuitaElPrepagoYAvisa()
        {
            _ = Aprobada(diasHastaElLimite: 0);

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Caducadas);
            Assert.AreEqual(EstadosPagoAPlazos.CADUCADO, Fila().Estado);
            CollectionAssert.AreEqual(new[] { "PAP-7" }, financiera.Canceladas);
            Assert.AreEqual(0, pedidos.Prepagos.Count, "El pedido vuelve a estar sin pagar");
            Assert.IsTrue(avisos.Avisos.Single().StartsWith($"Perdido:{PEDIDO}:El pedido no ha salido en el plazo de Financiera Falsa"));
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PlazoVencido_ElProveedorNoDejaCancelar_SeQuedaComoEstabaParaReintentar()
        {
            _ = Aprobada(diasHastaElLimite: -1);
            financiera.FallaAlCancelar = true;

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Fallidas);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, Fila().Estado);
            Assert.AreEqual(1, pedidos.Prepagos.Count);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PlazoVencidoAMedioServir_CobraLoServidoYAjustaElPrepago()
        {
            _ = Aprobada(diasHastaElLimite: -1);
            PedidoPagoAPlazos pedido = Pedido(total: 202.50M);
            pedido.Lineas.Add(Linea(100M, Constantes.EstadosLineaVenta.ALBARAN));
            pedidos.Pedidos[PEDIDO] = pedido;

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Capturadas);
            Assert.AreEqual(100M, financiera.Capturas.Single().Value);
            Assert.AreEqual(0, financiera.Canceladas.Count, "Con género ya entregado no se cancela la financiación");
            Assert.AreEqual(EstadosPagoAPlazos.CAPTURADO, Fila().Estado);
            Assert.AreEqual(100M, Fila().ImporteCapturado);
            Assert.AreEqual(100M, pedidos.Prepagos.Single().Importe);
            Assert.IsTrue(avisos.Avisos.Single().StartsWith($"Perdido:{PEDIDO}:se acabó el plazo"));
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PedidoBorradoConFinanciacionAprobada_SeCancela()
        {
            _ = Aprobada();
            _ = pedidos.Pedidos.Remove(PEDIDO);

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Canceladas);
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila().Estado);
            CollectionAssert.AreEqual(new[] { "PAP-7" }, financiera.Canceladas);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_PendienteQueElClienteNoTermina_Caduca_YSinPedido_SeCancela()
        {
            SolicitudPagoAPlazos vieja = Solicitud();
            vieja.FechaCreacion = AHORA.AddDays(-8);
            _ = repositorio.Anadir(vieja);
            SolicitudPagoAPlazos reciente = Solicitud(id: 8);
            reciente.Pedido = 930501;
            _ = repositorio.Anadir(reciente);
            pedidos.Pedidos[930501] = Pedido();
            SolicitudPagoAPlazos huerfana = Solicitud(id: 9);
            huerfana.Pedido = 930502;
            _ = repositorio.Anadir(huerfana);

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Caducadas);
            CollectionAssert.AreEqual(new[] { 9 }, resultado.Canceladas);
            Assert.AreEqual(EstadosPagoAPlazos.CADUCADO, Fila(7).Estado);
            Assert.AreEqual(EstadosPagoAPlazos.PENDIENTE, Fila(8).Estado);
            Assert.AreEqual(EstadosPagoAPlazos.CANCELADO, Fila(9).Estado);
            Assert.AreEqual(0, financiera.Canceladas.Count, "Pendientes: el proveedor no había aprobado nada");

            // Caducada: si después el proveedor la aprobara, se le dice que no
            Assert.AreEqual("Rechazar", (await Notificar("PAP-7", TipoNotificacionPagoAPlazos.PideConfirmacion)).Cuerpo);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_UnFalloNoParaLasDemas()
        {
            SolicitudPagoAPlazos rota = Aprobada();
            rota.Proveedor = "YaNoExiste";
            pedidos.Pedidos[PEDIDO] = Pedido(estado: Constantes.EstadosLineaVenta.ALBARAN);
            SolicitudPagoAPlazos buena = Solicitud(EstadosPagoAPlazos.APROBADO, id: 8);
            buena.Pedido = 930501;
            buena.FechaLimiteCaptura = AHORA.AddDays(5);
            _ = repositorio.Anadir(buena);
            pedidos.Pedidos[930501] = Pedido(estado: Constantes.EstadosLineaVenta.ALBARAN);

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Fallidas);
            CollectionAssert.AreEqual(new[] { 8 }, resultado.Capturadas);
        }

        [TestMethod]
        public async Task PagoAPlazos_Job_SinLaTabla_NoHaceNada()
        {
            _ = Aprobada(diasHastaElLimite: 0);
            repositorio.HayTabla = false;

            ResultadoPlazosPagoAPlazos resultado = await servicio.ProcesarPlazos(ahora);

            Assert.AreEqual(0, resultado.Caducadas.Count + resultado.Fallidas.Count);
            Assert.AreEqual(0, financiera.Canceladas.Count);
        }

        #endregion
    }
}
