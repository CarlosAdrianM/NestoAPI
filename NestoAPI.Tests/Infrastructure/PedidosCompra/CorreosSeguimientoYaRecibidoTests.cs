using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// 27/11/26. Caso real: correo de SEUR Id 38, recibido el 26/11 a las 12:55, «tu envío de IBEROCEL - PARAISO será entregado hoy», código
    /// A-2609942, RemitentePaquete «iberocel - paraiso». El proveedor es el 846 (IBEROCELULOSA MADRILEÑA), que no casa solo por el nombre. Su
    /// pedido, el 220985 (enviado el 23/11), se recibió entero antes de que nadie asignara el correo: Asignar daba 400 y no había forma de
    /// enseñar el remitente. Y el caso «postquam» → proveedor 451 sin pedidos en el aire, que en «por asignar» no decía de quién era.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoYaRecibidoTests
    {
        private static readonly DateTime CORREO = new DateTime(2026, 11, 26, 12, 55, 0);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 27, 9, 30, 0);
        private const string MENSAJE_DE_SIEMPRE_220999 = "El pedido de compra 220999 no tiene líneas enviadas y sin recibir: no se le puede poner la fecha del correo.";

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            errores = new List<Exception>();
            repositorio.Proveedores["1|846"] = "IBEROCELULOSA MADRILEÑA, S.L.";
            repositorio.Proveedores["1|451"] = "POSTQUAM COSMETIC, S.L.";
        }

        private ServicioAplicacionCorreosCompras Aplicacion() => new ServicioAplicacionCorreosCompras(repositorio, () => true, null,
            (empresa, productos, descripcion, causas) => { }, () => AHORA, errores.Add, _ => false);

        private CorreoSeguimientoFila CorreoIberocel(int id = 38, DateTime? recibido = null)
        {
            var fila = new CorreoSeguimientoFila
            {
                Id = id,
                MensajeId = "seur-" + id,
                Recibido = recibido ?? CORREO,
                FechaCaptura = recibido ?? CORREO,
                RemitenteDireccion = "notificaciones@seur.es",
                RemitenteDominio = "seur.es",
                Asunto = "Tu envío de IBEROCEL - PARAISO será entregado hoy",
                Agencia = "SEUR",
                Codigo = "A-2609942",
                RemitentePaquete = "iberocel - paraiso"
            };
            repositorio.Correos.Add(fila);
            return fila;
        }

        /// <summary>El 220985 de Iberocel, enviado el 23/11 y recibido entero el 26/11 (antes de asignar el correo).</summary>
        private void PedidoIberocelRecibido(DateTime? albaran = null)
        {
            repositorio.AnadirPedido(220985, "846", "IBEROCELULOSA MADRILEÑA, S.L.", new DateTime(2026, 11, 23, 10, 0, 0), new DateTime(2026, 11, 26));
            repositorio.Recibir(220985, albaran ?? new DateTime(2026, 11, 26));
        }

        private Task<ResultadoOperacionCorreoCompra> Asignar(int pedido, bool recordar = true, int correo = 38, bool reasignar = false) =>
            Aplicacion().Asignar(correo, new PeticionAsignarCorreoCompraDTO { Empresa = "1", Pedido = pedido, RecordarRemitente = recordar }, reasignar, "Carlos");

        private async Task<PorAsignarCorreosCompraDTO> PorAsignar(bool incluirLlegados = false, IEnumerable<RemitentePaqueteRecordado> memoria = null) =>
            ComprasAgenciasEntradaController.ComponerPorAsignar(await repositorio.LeerCorreos(AHORA.AddDays(-17)), await repositorio.LeerPedidosEnAire("1"),
                AHORA.Date.AddDays(-7), AHORA.Date, incluirLlegados: incluirLlegados, memoria: memoria ?? repositorio.RemitentesPaquete);

        #region La regla: ya recibido o no en el aire por otra causa

        private static EstadoPedidoCompraCorreo Estado(int enAire = 0, int sinEnviar = 0, int recibidas = 0, params DateTime[] albaranes) => new EstadoPedidoCompraCorreo
        {
            Empresa = "1", Pedido = 220985, Proveedor = "846", Lineas = enAire + sinEnviar + recibidas, LineasEnAire = enAire, LineasSinEnviar = sinEnviar,
            LineasRecibidas = recibidas, Albaranes = albaranes.ToList()
        };

        [TestMethod]
        public void Regla_SinLineas_NoExiste()
        {
            Assert.AreEqual(SituacionPedidoAsignado.NoExiste, ReglaPedidoYaRecibido.Clasificar(Estado(), CORREO));
            Assert.AreEqual(SituacionPedidoAsignado.NoExiste, ReglaPedidoYaRecibido.Clasificar(null, CORREO));
            Assert.AreEqual("No existe el pedido de compra 220985.", ReglaPedidoYaRecibido.MensajeNoSeAsigna(SituacionPedidoAsignado.NoExiste, Estado(), 220985));
        }

        [TestMethod]
        public void Regla_CasoIberocel_RecibidoEntero_EseMismoDia_YaRecibido()
        {
            EstadoPedidoCompraCorreo estado = Estado(recibidas: 4, albaranes: new DateTime(2026, 11, 26));
            Assert.AreEqual(SituacionPedidoAsignado.YaRecibido, ReglaPedidoYaRecibido.Clasificar(estado, CORREO));
            Assert.AreEqual(new DateTime(2026, 11, 26), ReglaPedidoYaRecibido.AlbaranDelEnvio(estado, CORREO));
            Assert.IsNull(ReglaPedidoYaRecibido.MensajeNoSeAsigna(SituacionPedidoAsignado.YaRecibido, estado, 220985));
        }

        [TestMethod]
        public void Regla_ConLineasEnElAire_EnElAire()
        {
            Assert.AreEqual(SituacionPedidoAsignado.EnElAire, ReglaPedidoYaRecibido.Clasificar(Estado(enAire: 1, recibidas: 2, albaranes: CORREO.Date), CORREO));
        }

        [TestMethod]
        public void Regla_NuncaEnviado_NoEnviado_ConElMensajeDeSiempre()
        {
            EstadoPedidoCompraCorreo estado = Estado(sinEnviar: 3);
            Assert.AreEqual(SituacionPedidoAsignado.NoEnviado, ReglaPedidoYaRecibido.Clasificar(estado, CORREO));
            Assert.AreEqual(MENSAJE_DE_SIEMPRE_220999, ReglaPedidoYaRecibido.MensajeNoSeAsigna(SituacionPedidoAsignado.NoEnviado, estado, 220999));
        }

        [TestMethod]
        public void Regla_RecibidoUnaParteYElRestoSinEnviar_YaRecibido()
        {
            Assert.AreEqual(SituacionPedidoAsignado.YaRecibido, ReglaPedidoYaRecibido.Clasificar(Estado(sinEnviar: 2, recibidas: 3, albaranes: new DateTime(2026, 11, 27)), CORREO));
        }

        [TestMethod]
        public void Regla_RecibidoUnMesAntesDelCorreo_FueraDePlazo()
        {
            EstadoPedidoCompraCorreo estado = Estado(recibidas: 2, albaranes: new DateTime(2026, 10, 20));
            Assert.AreEqual(SituacionPedidoAsignado.RecibidoFueraDePlazo, ReglaPedidoYaRecibido.Clasificar(estado, CORREO));
            StringAssert.Contains(ReglaPedidoYaRecibido.MensajeNoSeAsigna(SituacionPedidoAsignado.RecibidoFueraDePlazo, estado, 220985), "20/10/2026");
        }

        [TestMethod]
        public void Regla_RecibidoSinFechaDeAlbaran_FueraDePlazo_SinRomper()
        {
            EstadoPedidoCompraCorreo estado = Estado(recibidas: 2);
            Assert.AreEqual(SituacionPedidoAsignado.RecibidoFueraDePlazo, ReglaPedidoYaRecibido.Clasificar(estado, CORREO));
            StringAssert.Contains(ReglaPedidoYaRecibido.MensajeNoSeAsigna(SituacionPedidoAsignado.RecibidoFueraDePlazo, estado, 220985), "sin fecha de albarán");
        }

        [TestMethod]
        public void Regla_AlbaranDelEnvio_ElPrimeroDesdeElCorreo_Y_SiNoElUltimoDeAntesEnElMargen()
        {
            Assert.AreEqual(new DateTime(2026, 11, 27), ReglaPedidoYaRecibido.AlbaranDelEnvio(
                Estado(recibidas: 3, albaranes: new[] { new DateTime(2026, 11, 22), new DateTime(2026, 11, 28), new DateTime(2026, 11, 27) }), CORREO));
            Assert.AreEqual(new DateTime(2026, 11, 24), ReglaPedidoYaRecibido.AlbaranDelEnvio(
                Estado(recibidas: 2, albaranes: new[] { new DateTime(2026, 11, 22), new DateTime(2026, 11, 24) }), CORREO));
            Assert.AreEqual(new DateTime(2026, 11, 19), ReglaPedidoYaRecibido.AlbaranDelEnvio(
                Estado(recibidas: 1, albaranes: new DateTime(2026, 11, 19)), CORREO), "Una semana antes aún vale");
            Assert.IsNull(ReglaPedidoYaRecibido.AlbaranDelEnvio(Estado(recibidas: 1, albaranes: new DateTime(2026, 11, 18)), CORREO));
        }

        #endregion

        #region Asignar a un pedido ya recibido (caso Iberocel)

        [TestMethod]
        public async Task CasoIberocel_AsignarAlPedidoYaRecibido_Ok_SinTocarFechas_YEnseñaElRemitente()
        {
            CorreoIberocel();
            PedidoIberocelRecibido();

            ResultadoOperacionCorreoCompra r = await Asignar(220985);

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo, r.Mensaje);
            Assert.IsTrue(r.Respuesta.YaRecibido);
            Assert.AreEqual(new DateTime(2026, 11, 26), r.Respuesta.FechaAlbaran);
            Assert.AreEqual("846", r.Respuesta.Proveedor);
            Assert.AreEqual(0, r.Respuesta.LineasCambiadas);
            StringAssert.StartsWith(r.Mensaje, "El pedido 220985 ya estaba recibido: no se ha cambiado ninguna fecha; el envío queda asignado a él.");
            Assert.AreEqual(0, repositorio.Rastros.Count, "Ninguna fecha de compras tocada");
            CorreoSeguimientoFila correo = repositorio.Correo(38);
            Assert.AreEqual(220985, correo.PedidoReal);
            Assert.AreEqual("1", correo.EmpresaReal);
            Assert.AreEqual(new DateTime(2026, 11, 26), correo.FechaAlbaranReal);
            Assert.IsNull(correo.PedidoAplicado, "No se marca como aplicado: no se ha puesto ninguna fecha");
            Assert.AreEqual(1, repositorio.RastrosYaRecibido.Count);
            Assert.AreEqual("Carlos", repositorio.RastrosYaRecibido[0].Usuario);
            Assert.IsTrue(r.Respuesta.RemitenteRecordado);
            RemitentePaqueteRecordado recordado = repositorio.RemitentesPaquete.Single();
            Assert.AreEqual("iberocel - paraiso", recordado.RemitentePaquete);
            Assert.AreEqual("846", recordado.Proveedor);
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
        }

        [TestMethod]
        public async Task CasoIberocel_DespuesDeAsignarlo_YaNoSaleEnPorAsignar()
        {
            CorreoIberocel();
            PedidoIberocelRecibido();
            Assert.AreEqual(1, (await PorAsignar()).Envios.Count, "Antes, por asignar");

            _ = await Asignar(220985);

            Assert.AreEqual(0, (await PorAsignar()).Envios.Count);
            EnvioPorAsignarDTO llegado = (await PorAsignar(incluirLlegados: true)).Envios.Single();
            Assert.AreEqual("El pedido de compra 220985 ya se ha recibido el 26/11/2026.", llegado.MotivoYaNoPendiente);
        }

        [TestMethod]
        public async Task CasoIberocel_LosDemasCorreosDelEnvio_TambienQuedanConElPedido()
        {
            CorreoIberocel();
            CorreoIberocel(39, CORREO.AddHours(-20));
            PedidoIberocelRecibido();

            ResultadoOperacionCorreoCompra r = await Asignar(220985, recordar: false);

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo, r.Mensaje);
            Assert.AreEqual(1, r.Respuesta.CorreosDelMismoEnvio);
            Assert.AreEqual(220985, repositorio.Correo(39).PedidoReal);
            Assert.AreEqual(0, repositorio.RemitentesPaquete.Count, "Sin RecordarRemitente no se recuerda");
        }

        [TestMethod]
        public async Task PedidoQueNoExiste_400NoExiste()
        {
            CorreoIberocel();

            ResultadoOperacionCorreoCompra r = await Asignar(999999);

            Assert.AreEqual(CodigoOperacionCorreoCompra.NoEnElAire, r.Codigo, "El 400 de siempre");
            Assert.AreEqual("No existe el pedido de compra 999999.", r.Mensaje);
            Assert.IsNull(repositorio.Correo(38).PedidoReal);
            Assert.AreEqual(0, repositorio.RemitentesPaquete.Count);
        }

        [TestMethod]
        public async Task PedidoNuncaEnviado_El400DeSiempre_YNoSeTocaNada()
        {
            CorreoIberocel();
            repositorio.SinEnviar[220999] = 3;

            ResultadoOperacionCorreoCompra r = await Asignar(220999);

            Assert.AreEqual(CodigoOperacionCorreoCompra.NoEnElAire, r.Codigo);
            Assert.AreEqual(MENSAJE_DE_SIEMPRE_220999, r.Mensaje);
            Assert.IsNull(repositorio.Correo(38).PedidoReal);
            Assert.AreEqual(0, repositorio.RemitentesPaquete.Count);
            Assert.AreEqual(1, (await PorAsignar()).Envios.Count, "Sigue por asignar");
        }

        [TestMethod]
        public async Task PedidoRecibidoMuchoAntes_400_YSiguePorAsignar()
        {
            CorreoIberocel();
            PedidoIberocelRecibido(new DateTime(2026, 10, 20));

            ResultadoOperacionCorreoCompra r = await Asignar(220985);

            Assert.AreEqual(CodigoOperacionCorreoCompra.NoEnElAire, r.Codigo);
            StringAssert.Contains(r.Mensaje, "20/10/2026");
            Assert.IsNull(repositorio.Correo(38).PedidoReal);
        }

        [TestMethod]
        public async Task CorreoYaAplicadoAOtro_AlReasignarAUnoRecibido_SigueEl400()
        {
            CorreoSeguimientoFila fila = CorreoIberocel();
            fila.EmpresaAplicada = "1";
            fila.PedidoAplicado = 220001;
            PedidoIberocelRecibido();

            ResultadoOperacionCorreoCompra r = await Asignar(220985, reasignar: true);

            Assert.AreEqual(CodigoOperacionCorreoCompra.NoEnElAire, r.Codigo);
            Assert.IsNull(repositorio.Correo(38).PedidoReal);
        }

        [TestMethod]
        public async Task EnvioYaRecibidoConOtroPedido_409()
        {
            CorreoSeguimientoFila fila = CorreoIberocel();
            fila.EmpresaReal = "1";
            fila.PedidoReal = 220900;
            PedidoIberocelRecibido();

            ResultadoOperacionCorreoCompra r = await Asignar(220985);

            Assert.AreEqual(CodigoOperacionCorreoCompra.Conflicto, r.Codigo);
            Assert.AreEqual(220900, repositorio.Correo(38).PedidoReal);
        }

        [TestMethod]
        public async Task PedidoEnElAire_ComoSiempre_PoneLaFecha()
        {
            CorreoIberocel();
            repositorio.AnadirPedido(220985, "846", "IBEROCELULOSA MADRILEÑA, S.L.", new DateTime(2026, 11, 23, 10, 0, 0), new DateTime(2026, 12, 1));

            ResultadoOperacionCorreoCompra r = await Asignar(220985);

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo, r.Mensaje);
            Assert.IsFalse(r.Respuesta.YaRecibido);
            Assert.AreEqual(220985, repositorio.Correo(38).PedidoAplicado);
            Assert.IsNull(repositorio.Correo(38).PedidoReal);
            Assert.AreEqual(1, repositorio.Rastros.Count);
        }

        #endregion

        #region Enseñar un remitente del paquete a mano

        private Task<ResultadoOperacionCorreoCompra> Ensenar(string remitente, string proveedor, string empresa = "1") =>
            Aplicacion().EnsenarRemitentePaquete(new PeticionRemitentePaqueteDTO { RemitentePaquete = remitente, Empresa = empresa, Proveedor = proveedor }, "Carlos");

        [TestMethod]
        public async Task Ensenar_CasoIberocel_Nuevo_NormalizaComoElLector()
        {
            ResultadoOperacionCorreoCompra r = await Ensenar("  IBEROCEL - PARAÍSO ", "846");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo, r.Mensaje);
            Assert.AreEqual("iberocel - paraiso", r.RemitentePaquete.RemitentePaquete);
            Assert.AreEqual("846", r.RemitentePaquete.Proveedor);
            Assert.AreEqual("IBEROCELULOSA MADRILEÑA, S.L.", r.RemitentePaquete.NombreProveedor);
            Assert.IsTrue(r.RemitentePaquete.Nuevo);
            Assert.AreEqual("Desde ahora, lo que envíe «iberocel - paraiso» se propondrá para el proveedor 846 (IBEROCELULOSA MADRILEÑA, S.L.).", r.Mensaje);
            Assert.AreEqual("846", repositorio.RemitentesPaquete.Single().Proveedor);
            Assert.AreEqual(1, repositorio.RastrosRemitentePaquete.Count);
        }

        [TestMethod]
        public async Task Ensenar_YaApuntabaAOtro_LoCambiaYLoDice()
        {
            _ = await repositorio.GuardarRemitentePaquete("iberocel - paraiso", "1", "451", "(automático)");

            ResultadoOperacionCorreoCompra r = await Ensenar("iberocel - paraiso", "846");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo);
            Assert.IsFalse(r.RemitentePaquete.Nuevo);
            Assert.AreEqual("451", r.RemitentePaquete.ProveedorAnterior);
            Assert.AreEqual("«iberocel - paraiso» era del proveedor 451; desde ahora se propondrá para el proveedor 846 (IBEROCELULOSA MADRILEÑA, S.L.).", r.Mensaje);
            Assert.AreEqual("846", repositorio.RemitentesPaquete.Single().Proveedor);
        }

        [TestMethod]
        public async Task Ensenar_YaEraDeEse_NoTocaNada()
        {
            _ = await repositorio.GuardarRemitentePaquete("iberocel - paraiso", "1", "846", "Otro");

            ResultadoOperacionCorreoCompra r = await Ensenar("Iberocel - Paraiso", "846");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo);
            Assert.IsTrue(r.RemitentePaquete.SinCambios);
            Assert.AreEqual(0, repositorio.RastrosRemitentePaquete.Count);
            Assert.AreEqual("Otro", repositorio.RemitentesPaquete.Single().Usuario);
        }

        [TestMethod]
        public async Task Ensenar_ProveedorQueNoExiste_400()
        {
            ResultadoOperacionCorreoCompra r = await Ensenar("iberocel - paraiso", "99999");

            Assert.AreEqual(CodigoOperacionCorreoCompra.PeticionIncorrecta, r.Codigo);
            Assert.AreEqual("No existe el proveedor 99999 en la empresa 1.", r.Mensaje);
            Assert.AreEqual(0, repositorio.RemitentesPaquete.Count);
        }

        [DataTestMethod]
        [DataRow(null, "846", "1")]
        [DataRow("  - ", "846", "1")]
        [DataRow("iberocel", "", "1")]
        [DataRow("iberocel", "12345678901", "1")]
        [DataRow("iberocel", "846", "1234")]
        public async Task Ensenar_PeticionIncorrecta_400(string remitente, string proveedor, string empresa)
        {
            ResultadoOperacionCorreoCompra r = await Ensenar(remitente, proveedor, empresa);

            Assert.AreEqual(CodigoOperacionCorreoCompra.PeticionIncorrecta, r.Codigo);
            Assert.AreEqual(0, repositorio.RemitentesPaquete.Count);
        }

        [TestMethod]
        public async Task Ensenar_SinScript_503()
        {
            repositorio.ConAplicacion = false;

            Assert.AreEqual(CodigoOperacionCorreoCompra.SinTabla, (await Ensenar("iberocel - paraiso", "846")).Codigo);
        }

        #endregion

        #region Remitente recordado sin pedidos en el aire (caso «postquam» → 451)

        private CorreoSeguimientoFila CorreoPostquam()
        {
            var fila = new CorreoSeguimientoFila
            {
                Id = 41, MensajeId = "seur-41", Recibido = CORREO, FechaCaptura = CORREO, RemitenteDireccion = "notificaciones@seur.es", RemitenteDominio = "seur.es",
                Asunto = "Tu envío PV26-58001 de Postquam", Agencia = "SEUR", Codigo = "PV26-58001", RemitentePaquete = "postquam"
            };
            repositorio.Correos.Add(fila);
            return fila;
        }

        private static readonly RemitentePaqueteRecordado POSTQUAM = new RemitentePaqueteRecordado
        {
            Id = 7, RemitentePaquete = "postquam", Empresa = "1", Proveedor = "451", NombreProveedor = "POSTQUAM COSMETIC, S.L."
        };

        [TestMethod]
        public async Task Postquam_RecordadoSinPedidosEnElAire_DiceDeQuienEsYPorQue()
        {
            CorreoPostquam();

            EnvioPorAsignarDTO envio = (await PorAsignar(memoria: new[] { POSTQUAM })).Envios.Single();

            Assert.AreEqual("451", envio.ProveedorPropuesto);
            Assert.AreEqual("POSTQUAM COSMETIC, S.L.", envio.NombreProveedorPropuesto);
            Assert.AreEqual("Postquam es el proveedor 451 (POSTQUAM COSMETIC, S.L.), pero no tiene ningún pedido de compra enviado y sin recibir.", envio.Explicacion);
            Assert.AreEqual(0, envio.Candidatos.Count, "No se inventan candidatos");
        }

        [TestMethod]
        public async Task Postquam_SinRecordar_ComoAntes()
        {
            CorreoPostquam();

            EnvioPorAsignarDTO envio = (await PorAsignar(memoria: new RemitentePaqueteRecordado[0])).Envios.Single();

            Assert.IsNull(envio.ProveedorPropuesto);
            Assert.IsNull(envio.NombreProveedorPropuesto);
            Assert.IsNull(envio.Explicacion);
        }

        [TestMethod]
        public async Task Postquam_RecordadoDespuesDeAnalizar_ConUnPedidoEnElAire_LoNombra()
        {
            CorreoPostquam();
            repositorio.AnadirPedido(220990, "451", "POSTQUAM COSMETIC, S.L.", new DateTime(2026, 11, 20), new DateTime(2026, 11, 28));

            EnvioPorAsignarDTO envio = (await PorAsignar(memoria: new[] { POSTQUAM })).Envios.Single();

            Assert.AreEqual("451", envio.ProveedorPropuesto);
            Assert.AreEqual("Postquam es el proveedor 451 (POSTQUAM COSMETIC, S.L.) y tiene un pedido de compra enviado y sin recibir, el 220990. " +
                "El correo se analizó antes de saberlo: asígnelo a mano.", envio.Explicacion);
        }

        [TestMethod]
        public void Regla_ConCandidatos_NoDiceNada()
        {
            var fila = new CorreoSeguimientoFila { Id = 1, Recibido = CORREO, RemitentePaquete = "postquam" };
            Assert.IsNull(ReglaRemitenteRecordadoSinCandidatos.Explicar(new[] { fila },
                new[] { new CandidatoPedidoCompra { Empresa = "1", Pedido = 220990, Proveedor = "451" } }, new[] { POSTQUAM }, null));
        }

        [TestMethod]
        public async Task CasoIberocel_EnsenadoAMano_ElEnvioSiguienteDiceDeQuienEs()
        {
            _ = await Ensenar("IBEROCEL - PARAISO", "846");
            repositorio.RemitentesPaquete.Single().NombreProveedor = "IBEROCELULOSA MADRILEÑA, S.L."; // lo pone la consulta de la memoria
            CorreoIberocel();

            EnvioPorAsignarDTO envio = (await PorAsignar()).Envios.Single();

            Assert.AreEqual("846", envio.ProveedorPropuesto);
            Assert.AreEqual("Iberocel - paraiso es el proveedor 846 (IBEROCELULOSA MADRILEÑA, S.L.), pero no tiene ningún pedido de compra enviado y sin recibir.",
                envio.Explicacion);
        }

        #endregion
    }
}
