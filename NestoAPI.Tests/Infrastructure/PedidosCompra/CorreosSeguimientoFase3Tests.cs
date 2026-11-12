using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857 (fase 3): las reglas puras (inequívoca, fecha, remitente del paquete, memoria, mismo envío, sugerido).</summary>
    [TestClass]
    public class CorreosSeguimientoFase3Tests
    {
        internal const string UPS_TEXTO = @"¡Tu paquete está de camino!
De MBE STORE C/O MULTIBOXES S.L

Entrega estimada
Lunes 16/11/2026
entre 10:45 - 14:45

Cambiar entrega

UPS Standard®
{0}";

        /// <summary>El correo real de MBE del 12/11/26 (el proveedor y los tres códigos de UPS).</summary>
        internal const string MBE_TEXTO = @"Tus envíos MBE
Envío: 1
Remitente:
Weelko Barcelona S. L.
Camí de Garreta, 92 Pere Bozzo
08600 BERGA BARCELONA
España
Destinatario:
NUEVA VISION, SA
SANTI
Rio Tietar, 11 nave 22
28110 ALGETE MADRID
España
Envío:
Servicio: MBE Standard
Peso: 27,0 kg/vol.
N. de paquetes: 3
Ref.: SO252231WKOUT77626
Tracking MBE: ES0061-0L-000000VTHP
Courier: UPS
Tracking: 1Z21E7X76890915153
Detalles:
Tracking: 1Z21E7X76890915153- estado:Etiqueta creada
Tracking: 1Z21E7X76890089163- estado:Etiqueta creada
Tracking: 1Z21E7X76893119777- estado:Etiqueta creada
Siga su envío<https://www.mbe.es/es/tracking?c=1Z21E7X76890915153>";

        private static readonly DateTime HOY = new DateTime(2026, 11, 12);

        internal static CorreoCompras Correo(string texto, string remitente, DateTime recibido, string asunto = "Su envío", string id = null) => new CorreoCompras
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            MensajeId = "<" + (id ?? Guid.NewGuid().ToString("N")) + "@ejemplo.test>",
            Recibido = recibido,
            RemitenteDireccion = remitente,
            Asunto = asunto,
            Cuerpo = texto,
            CuerpoEsHtml = false
        };

        private static CorreoSeguimientoFila Fila(int id, DateTime recibido, params string[] codigos) => new CorreoSeguimientoFila
        {
            Id = id,
            MensajeId = "m" + id,
            Recibido = recibido,
            Codigo = codigos.FirstOrDefault(),
            Seguimientos = codigos.Length > 1 ? JsonConvert.SerializeObject(codigos.Select(c => new SeguimientoCorreo { Codigo = c })) : null
        };

        private static List<CandidatoPedidoCompra> Candidatos(params int[] pedidos) =>
            pedidos.Select(p => new CandidatoPedidoCompra { Empresa = "1", Pedido = p, Proveedor = "9100", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE }).ToList();

        #region Inequívoca y fecha

        [TestMethod]
        public void Inequivoca_SoloConUnPedidoYSiNoEsSalidaNuestra()
        {
            Assert.IsNull(ReglaAsignacionInequivoca.Pedido(false, Candidatos()));
            Assert.AreEqual(912001, ReglaAsignacionInequivoca.Pedido(false, Candidatos(912001)).Pedido);
            Assert.AreEqual(912001, ReglaAsignacionInequivoca.Pedido(null, Candidatos(912001, 912001)).Pedido, "El mismo pedido repetido es uno");
            Assert.IsNull(ReglaAsignacionInequivoca.Pedido(false, Candidatos(912001, 912007)));
            Assert.IsNull(ReglaAsignacionInequivoca.Pedido(true, Candidatos(912001)), "Una salida nuestra nunca");
        }

        [TestMethod]
        public void Fecha_LaEntregaQueDiceElCorreo()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), ReglaFechaRecepcionPorCorreo.Calcular(new DateTime(2026, 11, 16), HOY.AddHours(20), 24, HOY));
        }

        [TestMethod]
        public void Fecha_SinEntrega_RecibidoMasLasHorasDeLaAgencia_Y48SinAgencia()
        {
            DateTime recibido = HOY.AddHours(20);
            Assert.AreEqual(new DateTime(2026, 11, 13), ReglaFechaRecepcionPorCorreo.Calcular(null, recibido, 24, HOY));
            Assert.AreEqual(new DateTime(2026, 11, 14), ReglaFechaRecepcionPorCorreo.Calcular(null, recibido, null, HOY));
            Assert.AreEqual(new DateTime(2026, 11, 13), ReglaFechaRecepcionPorCorreo.Calcular(new CorreoSeguimientoFila { Recibido = recibido, Agencia = "GLS" }, HOY));
            Assert.AreEqual(new DateTime(2026, 11, 14), ReglaFechaRecepcionPorCorreo.Calcular(new CorreoSeguimientoFila { Recibido = recibido, Agencia = "Dachser" }, HOY));
            Assert.AreEqual(new DateTime(2026, 11, 14), ReglaFechaRecepcionPorCorreo.Calcular(new CorreoSeguimientoFila { Recibido = recibido, Agencia = "Transportes inventados" }, HOY));
        }

        [TestMethod]
        public void Fecha_NuncaAntesDeHoy()
        {
            Assert.AreEqual(HOY, ReglaFechaRecepcionPorCorreo.Calcular(new DateTime(2026, 11, 2), HOY.AddDays(-12), 24, HOY));
            Assert.AreEqual(HOY, ReglaFechaRecepcionPorCorreo.Calcular(null, HOY.AddDays(-5), 24, HOY));
        }

        [TestMethod]
        public void Fecha_DelEnvio_LaEntregaMasTardiaDeSusCorreos_YSiNingunaLaDiceElMasReciente()
        {
            var envio = new List<CorreoSeguimientoFila>
            {
                new CorreoSeguimientoFila { Recibido = HOY.AddHours(20), Agencia = "UPS", EntregaEstimadaDia = new DateTime(2026, 11, 16) },
                new CorreoSeguimientoFila { Recibido = HOY.AddHours(20.1), Agencia = "UPS" },
                new CorreoSeguimientoFila { Recibido = HOY.AddHours(21), Agencia = "UPS", EntregaEstimadaDia = new DateTime(2026, 11, 17) }
            };
            Assert.AreEqual(new DateTime(2026, 11, 17), ReglaFechaRecepcionPorCorreo.Calcular(envio, HOY));
            Assert.AreEqual(new DateTime(2026, 11, 13), ReglaFechaRecepcionPorCorreo.Calcular(envio.Take(2).Skip(1), HOY));
        }

        [TestMethod]
        public void Reciente_TresDias()
        {
            DateTime ahora = HOY.AddHours(10);
            Assert.IsTrue(ReglaFechaRecepcionPorCorreo.Reciente(ahora.AddDays(-3), ahora));
            Assert.IsFalse(ReglaFechaRecepcionPorCorreo.Reciente(ahora.AddDays(-3).AddMinutes(-1), ahora));
        }

        [TestMethod]
        public void Bloqueo_YaAplicadoAlMismo_AOtro_YReasignar()
        {
            Assert.IsNull(ReglaAplicacionCorreo.Bloqueo(null, null, "1", 912001, false));
            Assert.AreEqual(EstadoAplicacionFecha.YaAplicadoAlMismo, ReglaAplicacionCorreo.Bloqueo("1  ", 912001, "1", 912001, false));
            Assert.AreEqual(EstadoAplicacionFecha.YaAplicadoAlMismo, ReglaAplicacionCorreo.Bloqueo("1", 912001, "1", 912001, true));
            Assert.AreEqual(EstadoAplicacionFecha.AplicadoAOtro, ReglaAplicacionCorreo.Bloqueo("1", 912001, "1", 912007, false));
            Assert.IsNull(ReglaAplicacionCorreo.Bloqueo("1", 912001, "1", 912007, true));
        }

        [TestMethod]
        public void QueCambian_LasQueNoTienenYaEseDia()
        {
            var lineas = new List<LineaCompraCorreo>
            {
                new LineaCompraCorreo { NumeroOrden = 1, FechaRecepcion = new DateTime(2026, 11, 16, 9, 30, 0) },
                new LineaCompraCorreo { NumeroOrden = 2, FechaRecepcion = new DateTime(2026, 11, 20) },
                new LineaCompraCorreo { NumeroOrden = 3, FechaRecepcion = null }
            };
            CollectionAssert.AreEqual(new[] { 2, 3 }, ReglaAplicacionCorreo.QueCambian(lineas, new DateTime(2026, 11, 16)).Select(l => l.NumeroOrden).ToList());
        }

        [TestMethod]
        public void Rastro_LlevaCorreoAgenciaCodigoFechasYSiEligioElSugerido()
        {
            var peticion = new PeticionAplicacionFecha
            {
                CorreoId = 7, Empresa = "1", Pedido = 912001, Fecha = new DateTime(2026, 11, 16), Agencia = "UPS", Codigo = "1Z21E7X76890089163",
                Automatico = false, AplicadoPor = "Prueba", EligioSugerido = true
            };
            string anterior = RastroAplicacionFecha.Anterior(peticion, new[] { new LineaCompraCorreo { NumeroOrden = 5, Producto = "17404", FechaRecepcion = new DateTime(2026, 11, 20) } });
            string nuevo = RastroAplicacionFecha.Nuevo(peticion, 1, "1", 912000);
            StringAssert.Contains(anterior, "2026-11-20");
            StringAssert.Contains(nuevo, "2026-11-16");
            StringAssert.Contains(nuevo, "\"CorreoId\":7");
            StringAssert.Contains(nuevo, "\"EligioSugerido\":true");
            StringAssert.Contains(nuevo, "1/912000");
        }

        #endregion

        #region Remitente del paquete

        [TestMethod]
        public void RemitentePaquete_CasoRealUps_LaLineaDeTrasElTitular()
        {
            Assert.AreEqual("mbe store c/o multiboxes s.l", ExtractorRemitentePaquete.Extraer(string.Format(UPS_TEXTO, "1Z21E7X76890089163")));
        }

        [TestMethod]
        public void RemitentePaquete_CasoRealMbe_ElValorEnLaLineaSiguienteALaEtiqueta()
        {
            Assert.AreEqual("weelko barcelona s. l", ExtractorRemitentePaquete.Extraer(MBE_TEXTO));
        }

        [DataTestMethod]
        [DataRow("Remitente: Cosméticos  Inventados, S.L.   Destinatario: NUEVA VISION", "cosmeticos inventados, s.l")]
        [DataRow("Expedidor: LABORATORIOS EJEMPLO SA", "laboratorios ejemplo sa")]
        [DataRow("Shipper: Example Beauty Ltd", "example beauty ltd")]
        [DataRow("Hola\nFrom: Proveedor Inventado <pedidos@proveedor-inventado.es>", "proveedor inventado")]
        public void RemitentePaquete_PorEtiqueta(string texto, string esperado)
        {
            Assert.AreEqual(esperado, ExtractorRemitentePaquete.Extraer(texto));
        }

        [DataTestMethod]
        [DataRow("Remitente: NUEVA VISION, SA")]
        [DataRow("Remitente:\nNueva Visión S.A.")]
        [DataRow("Enviado por: SEUR")]
        [DataRow("Destinatario:\nNUEVA VISION, SA")]
        [DataRow("Su pedido está en camino.")]
        [DataRow("De lunes a viernes")]
        public void RemitentePaquete_NiNosotrosNiUnaAgenciaASecas(string texto)
        {
            Assert.IsNull(ExtractorRemitentePaquete.Extraer(texto));
        }

        [TestMethod]
        public void RemitentePaquete_Normalizar()
        {
            Assert.AreEqual("mbe store c/o multiboxes s.l", ExtractorRemitentePaquete.Normalizar("  MBE Store C/O  Multiboxes S.L. "));
            Assert.IsNull(ExtractorRemitentePaquete.Normalizar(" .. "));
        }

        #endregion

        #region Salida nuestra y el correo de MBE

        [TestMethod]
        public void SalidaNuestra_MiraLaLineaSiguiente_RemitenteSiDestinatarioNo()
        {
            Assert.IsFalse(EnvioNuestroDeSalida.Es(MBE_TEXTO), "Nosotros de destinatario no es una salida");
            Assert.IsTrue(EnvioNuestroDeSalida.Es("Tus envíos MBE\nRemitente:\nNUEVA VISION, SA\nDestinatario:\nPeluquería Inventada"));
        }

        [TestMethod]
        public void CorreoMbe_AgenciaUps_LosTresCodigosYElDeMbe()
        {
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(
                Correo(MBE_TEXTO, "recogidas061@mbe.es", HOY.AddHours(20.25), "Sus envíos con MBE 12/11/2026"), AgenciasEntradaRegistradas.Todas, null);

            Assert.AreEqual("UPS", analisis.Agencia);
            List<string> codigos = analisis.Seguimientos.Select(s => s.Codigo).ToList();
            CollectionAssert.IsSubsetOf(new[] { "1Z21E7X76890915153", "1Z21E7X76890089163", "1Z21E7X76893119777", "ES0061-0L-000000VTHP" }, codigos);
            Assert.AreEqual("1Z21E7X76890915153", codigos.First());
            Assert.IsFalse(analisis.EsSalidaNuestra);
            Assert.AreEqual("weelko barcelona s. l", analisis.RemitentePaquete);
        }

        [TestMethod]
        public void CorreoMbe_CasaPorNombreConWeelkoBarcelonaSlu()
        {
            CorreoCompras correo = Correo(MBE_TEXTO, "recogidas061@mbe.es", HOY.AddHours(20.25));
            var pedidos = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9100", NombreProveedor = "WEELKO BARCELONA SLU" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912050, Proveedor = "9200", NombreProveedor = "COSMÉTICOS INVENTADOS, S.L." }
            };
            AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(correo, null, pedidos);

            Assert.AreEqual("9100", asignacion.ProveedorPropuesto);
            Assert.AreEqual(912001, asignacion.Candidatos.Single().Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_NOMBRE, asignacion.Candidatos.Single().Motivo);
        }

        [DataTestMethod]
        [DataRow("WEELKO BARCELONA SLU")]
        [DataRow("Weelko Barcelona, S.L.U.")]
        [DataRow("WEELKO BARCELONA S. L.")]
        [DataRow("Weelko Barcelona S.A.U.")]
        [DataRow("WEELKO BARCELONA, S.COOP.")]
        [DataRow("WEELKO BARCELONA C.B.")]
        [DataRow("Weelko Barcelona GmbH")]
        public void NombreSinSociedad_QuitaLaFormaSocietariaEntera(string nombre)
        {
            Assert.AreEqual("WEELKO BARCELONA", AsignadorPedidosCompra.NombreSinSociedad(nombre));
        }

        #endregion

        #region Memoria de remitentes del paquete

        [TestMethod]
        public void Asignador_RemitenteRecordado_TodosLosPedidosDeEseProveedor_TrasElNumero()
        {
            CorreoCompras correo = Correo(string.Format(UPS_TEXTO, "1Z21E7X76890089163"), "pkginfo@ups.com", HOY.AddHours(20));
            var pedidos = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9100", NombreProveedor = "WEELKO BARCELONA SLU" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912002, Proveedor = "9100", NombreProveedor = "WEELKO BARCELONA SLU" },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912050, Proveedor = "9200", NombreProveedor = "OTRO PROVEEDOR" }
            };
            var recordados = new List<RemitentePaqueteRecordado> { new RemitentePaqueteRecordado { RemitentePaquete = "mbe store c/o multiboxes s.l", Empresa = "1", Proveedor = "9100" } };

            Assert.IsFalse(AsignadorPedidosCompra.Asignar(correo, null, pedidos).Candidatos.Any(), "Sin memoria, UPS no dice el proveedor");
            AsignacionPedidoCompra asignacion = AsignadorPedidosCompra.Asignar(correo, null, pedidos, "MBE Store C/O Multiboxes S.L.", recordados);
            CollectionAssert.AreEquivalent(new[] { 912001, 912002 }, asignacion.Candidatos.Select(c => c.Pedido).ToList());
            Assert.IsTrue(asignacion.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_REMITENTE_PAQUETE));
            Assert.AreEqual("9100", asignacion.ProveedorPropuesto);

            CorreoCompras conNumero = Correo("Su pedido 912050 sale hoy. " + string.Format(UPS_TEXTO, "1Z21E7X76890089163"), "pkginfo@ups.com", HOY);
            Assert.AreEqual(912050, AsignadorPedidosCompra.Asignar(conNumero, null, pedidos, "mbe store c/o multiboxes s.l", recordados).Candidatos.Single().Pedido,
                "El número de pedido manda");
        }

        #endregion

        #region Mismo envío

        [TestMethod]
        public void MismoEnvio_SeEnlazanPorCualquierCodigoComun_YTransitivamente()
        {
            CorreoSeguimientoFila ups1 = Fila(1, HOY.AddHours(20.25), "1Z21E7X76890915153");
            CorreoSeguimientoFila mbe = Fila(2, HOY.AddHours(20.26), "1Z21E7X76890915153", "1Z21E7X76890089163", "1Z21E7X76893119777");
            CorreoSeguimientoFila ups2 = Fila(3, HOY.AddHours(20.27), "1Z21E7X76890089163");
            CorreoSeguimientoFila ups3 = Fila(4, HOY.AddHours(21), "1z21e7x76893119777");
            CorreoSeguimientoFila otro = Fila(5, HOY.AddHours(21), "1Z999AA10123456784");

            List<List<CorreoSeguimientoFila>> envios = MismoEnvio.Agrupar(new[] { ups3, otro, ups1, mbe, ups2 });

            Assert.AreEqual(2, envios.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, envios.Single(e => e.Count == 4).Select(f => f.Id).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, MismoEnvio.Grupo(ups2, new[] { ups1, mbe, ups3, otro }).Select(f => f.Id).ToList());
        }

        [TestMethod]
        public void MismoEnvio_CodigosDistintos_SalidasNuestrasOMasDeDiezDias_NoSeEnlazan()
        {
            CorreoSeguimientoFila a = Fila(1, HOY, "1Z21E7X76890915153");
            Assert.IsFalse(MismoEnvio.Comparten(a, Fila(2, HOY, "1Z21E7X76890089163")));
            Assert.IsFalse(MismoEnvio.Comparten(a, Fila(3, HOY.AddDays(10), "1Z21E7X76890915153")));
            Assert.IsTrue(MismoEnvio.Comparten(a, Fila(4, HOY.AddDays(9.9), "1Z21E7X76890915153")));
            CorreoSeguimientoFila salida = Fila(5, HOY, "1Z21E7X76890915153");
            salida.EsSalidaNuestra = true;
            Assert.IsFalse(MismoEnvio.Comparten(a, salida));
        }

        [TestMethod]
        public void Decidir_LosCandidatosPropiosMasConcretos_YLaEntregaMasTardia()
        {
            CorreoSeguimientoFila ups = Fila(1, HOY.AddHours(20), "1Z21E7X76890915153");
            ups.EntregaEstimadaDia = new DateTime(2026, 11, 16);
            CorreoSeguimientoFila mbe = Fila(2, HOY.AddHours(20.1), "1Z21E7X76890915153");
            mbe.PedidosCandidatos = AsignadorPedidosCompra.Serializar(Candidatos(912001, 912002));

            DecisionEnvio decision = MismoEnvio.Decidir(new[] { ups, mbe });

            CollectionAssert.AreEqual(new[] { 912001, 912002 }, decision.Candidatos.Select(c => c.Pedido).ToList());
            Assert.IsTrue(decision.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO));
            Assert.AreEqual("9100", decision.ProveedorPropuesto);
            Assert.IsNull(decision.Unico);
            Assert.AreEqual(new DateTime(2026, 11, 16), decision.EntregaEstimadaDia);
            Assert.IsTrue(MismoEnvio.Hereda(ups, decision));
            Assert.IsFalse(MismoEnvio.Hereda(mbe, decision), "El que tiene propios no hereda");
        }

        [TestMethod]
        public void Decidir_SiUnoYaEstaAplicado_EsePedidoEsElDelEnvio()
        {
            CorreoSeguimientoFila mbe = Fila(2, HOY.AddHours(20.1), "1Z21E7X76890915153");
            mbe.PedidosCandidatos = AsignadorPedidosCompra.Serializar(Candidatos(912001, 912002));
            mbe.EmpresaAplicada = "1";
            mbe.PedidoAplicado = 912002;
            CorreoSeguimientoFila ups = Fila(3, HOY.AddHours(21), "1Z21E7X76890915153");
            ups.PedidosCandidatos = AsignadorPedidosCompra.Serializar(Candidatos(912001));

            DecisionEnvio decision = MismoEnvio.Decidir(new[] { mbe, ups });

            Assert.AreEqual(912002, decision.Unico.Pedido);
            Assert.AreEqual("9100", decision.ProveedorPropuesto);
            Assert.IsTrue(MismoEnvio.Hereda(ups, decision), "Con el envío aplicado, ese pedido es su único candidato aunque tenga propios");
        }

        #endregion

        #region Sugerido

        [TestMethod]
        public void Sugerido_ElEnviadoMasRecienteAntesDelPrimerCorreo()
        {
            var pedidos = new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 911500, FechaEnvio = new DateTime(2026, 9, 7, 10, 0, 0) },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, FechaEnvio = new DateTime(2026, 11, 11, 7, 39, 0) },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912100, FechaEnvio = new DateTime(2026, 11, 12, 21, 0, 0) },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912200 }
            };
            Assert.AreEqual(912001, ReglaPedidoSugerido.Sugerido(pedidos, new DateTime(2026, 11, 12, 20, 15, 39)).Pedido);
            Assert.AreEqual(911500, ReglaPedidoSugerido.Sugerido(pedidos, new DateTime(2026, 11, 10)).Pedido);
            Assert.IsNull(ReglaPedidoSugerido.Sugerido(pedidos, new DateTime(2026, 9, 1)));
        }

        #endregion
    }
}
