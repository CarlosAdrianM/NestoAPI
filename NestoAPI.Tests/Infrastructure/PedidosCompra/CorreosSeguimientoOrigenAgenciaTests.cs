using FakeItEasy;
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
    /// NestoAPI#857i (Carlos 25/11/26): el aviso de DB Schenker «El remitente [en blanco] ha generado un nuevo envío para usted con número
    /// ESVIC262013788» no trae remitente, pedido ni referencia; la delegación de origen (VIC = Vic) va en el código y se aprende de qué proveedor
    /// es lo que sale de allí. Con los retoques del mismo día: remitente de DHL, recordar el remitente por otro criterio, aviso por asignar a su
    /// hora y los envíos que ya no están pendientes.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoOrigenAgenciaTests
    {
        private static readonly DateTime CORREO = new DateTime(2026, 11, 25, 9, 40, 0);
        private const string SCHENKER = "DB Schenker";

        private static List<OrigenEnvioAgencia> Vic() => new List<OrigenEnvioAgencia> { new OrigenEnvioAgencia { Agencia = SCHENKER, Origen = "VIC", Nombre = "Vic" } };

        private static OrigenAgenciaRecordado Recordado(string proveedor, string nombre = null, DateTime? ultima = null) => new OrigenAgenciaRecordado
        {
            Agencia = SCHENKER, Origen = "VIC", Empresa = "1", Proveedor = proveedor, NombreProveedor = nombre, Veces = 1, UltimaVez = ultima ?? CORREO.AddDays(-20)
        };

        private static PedidoCompraEnAire Pedido(int numero, string proveedor, string nombre, DateTime enviado, bool control = false) => new PedidoCompraEnAire
        {
            Empresa = "1", Pedido = numero, Proveedor = proveedor, NombreProveedor = nombre, FechaEnvio = enviado, LineasPendientes = 1, ControlPendientes = control
        };

        #region La agencia dice el origen

        [TestMethod]
        public void Schenker_SacaLaDelegacionDelCodigo_YSabeElNombreDeLasConocidas()
        {
            var schenker = new AgenciaEntradaSchenker();

            Assert.AreEqual("VIC", schenker.OrigenDelEnvio("ESVIC262013788"));
            Assert.AreEqual("Vic", schenker.NombreOrigen("VIC"));
            Assert.AreEqual("MNR", schenker.OrigenDelEnvio("esmnr264773770"));
            Assert.AreEqual("Manresa", schenker.NombreOrigen("MNR"));
            Assert.AreEqual("XYZ", schenker.OrigenDelEnvio("ESXYZ123456789"), "Una delegación desconocida también se aprende");
            Assert.IsNull(schenker.NombreOrigen("XYZ"));
            Assert.IsNull(schenker.OrigenDelEnvio("4313144450"));
            Assert.IsNull(schenker.OrigenDelEnvio("ESVIC26201378"), "Con 8 cifras no es su número de envío");
            Assert.IsNull(new AgenciaEntradaDhl().OrigenDelEnvio("ESVIC262013788"), "Solo la clase de la agencia sabe leer su código");
        }

        [TestMethod]
        public void OrigenesDeUnCorreoGuardado_YElTextoQueSeEnsena()
        {
            var fila = new CorreoSeguimientoFila { Agencia = SCHENKER, Codigo = "ESVIC262013788" };
            List<OrigenEnvioAgencia> origenes = OrigenesAgenciaCorreo.De(fila);

            Assert.AreEqual("VIC", origenes.Single().Origen);
            Assert.AreEqual("Vic", OrigenesAgenciaCorreo.Texto(origenes));
            Assert.AreEqual("XYZ", OrigenesAgenciaCorreo.Texto(OrigenesAgenciaCorreo.De(new CorreoSeguimientoFila { Agencia = SCHENKER, Codigo = "ESXYZ123456789" })));
            Assert.IsFalse(OrigenesAgenciaCorreo.De(new CorreoSeguimientoFila { Agencia = "DHL", Codigo = "4313144450" }).Any());
            Assert.AreEqual("Vic", ComprasAgenciasEntradaController.Dto(new CorreoSeguimientoFila { Id = 1, Agencia = SCHENKER, Codigo = "ESVIC262013788" }).OrigenAgencia);
        }

        #endregion

        #region LA REGLA

        [TestMethod]
        public void NadaRecordado_ComoAntes()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire> { Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3)) };

            Assert.IsNull(ReglaOrigenAgencia.Asignar(Vic(), new List<OrigenAgenciaRecordado>(), pedidos, CORREO));
            Assert.IsNull(ReglaOrigenAgencia.Asignar(Vic(), new[] { Recordado("77", ultima: CORREO.AddMonths(-13)) }, pedidos, CORREO), "Más de 12 meses no cuenta");
            var otraAgencia = Recordado("77");
            otraAgencia.Agencia = "DHL";
            Assert.IsNull(ReglaOrigenAgencia.Asignar(Vic(), new[] { otraAgencia }, pedidos, CORREO));
        }

        [TestMethod]
        public void UnProveedorConUnPedidoEnviadoAntes_EseInequivoco()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire>
            {
                Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3)),
                Pedido(220999, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddHours(1)), // enviado DESPUÉS del correo: no puede ser
                Pedido(221000, "88", "OTRO, S.L.", CORREO.AddDays(-2))
            };

            AsignacionPedidoCompra a = ReglaOrigenAgencia.Asignar(Vic(), new[] { Recordado("77") }, pedidos, CORREO);

            CandidatoPedidoCompra unico = a.Candidatos.Single();
            Assert.AreEqual(220989, unico.Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA, unico.Motivo);
            Assert.AreEqual("DB Schenker desde Vic: las otras veces era de LABORATORIOS EJEMPLO, S.A.; se asigna a su único pedido en el aire, 220989.", unico.Explicacion);
            Assert.AreEqual("77", a.ProveedorPropuesto);
        }

        [TestMethod]
        public void UnProveedorConVariosPedidos_PorAsignarConSusPedidos()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire>
            {
                Pedido(220980, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-5)),
                Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3)),
                Pedido(221000, "88", "OTRO, S.L.", CORREO.AddDays(-2))
            };

            AsignacionPedidoCompra a = ReglaOrigenAgencia.Asignar(Vic(), new[] { Recordado("77") }, pedidos, CORREO);

            CollectionAssert.AreEquivalent(new[] { 220980, 220989 }, a.Candidatos.Select(c => c.Pedido).ToList());
            Assert.IsTrue(a.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA));
            Assert.AreEqual("DB Schenker desde Vic: las otras veces era de LABORATORIOS EJEMPLO, S.A., que tiene 2 pedidos en el aire (220980 y 220989): elija el pedido.",
                a.Candidatos[0].Explicacion);
        }

        [TestMethod]
        public void UnProveedorConVariosYControlDePendientes_DecideLaReglaDe897()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire>
            {
                Pedido(220980, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-5), control: true),
                Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3), control: true)
            };

            AsignacionPedidoCompra a = ReglaControlPendientes.Completar(ReglaOrigenAgencia.Asignar(Vic(), new[] { Recordado("77") }, pedidos, CORREO), pedidos, CORREO, 30);

            Assert.AreEqual(220980, a.Candidatos.Single().Pedido, "El en curso más antiguo");
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES, a.Candidatos.Single().Motivo);
        }

        [TestMethod]
        public void VariosRecordados_SoloUnoConUnPedido_Ese_DiciendoQueLosOtrosNoTienenNada()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire> { Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3)) };

            AsignacionPedidoCompra a = ReglaOrigenAgencia.Asignar(Vic(), new[] { Recordado("77"), Recordado("88", "COSMETICA DEL BAGES, S.L.") }, pedidos, CORREO);

            Assert.AreEqual(220989, a.Candidatos.Single().Pedido);
            Assert.AreEqual("Desde Vic nos mandan por DB Schenker LABORATORIOS EJEMPLO, S.A. y COSMETICA DEL BAGES, S.L.; COSMETICA DEL BAGES, S.L. no tiene nada pendiente: " +
                "se asigna al único pedido en el aire de LABORATORIOS EJEMPLO, S.A., 220989.", a.Candidatos.Single().Explicacion);
        }

        [TestMethod]
        public void VariosRecordadosConPedidos_NuncaPorFrecuencia_PorAsignarSoloConEllos()
        {
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire>
            {
                Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3), control: true),
                Pedido(220990, "88", "COSMETICA DEL BAGES, S.L.", CORREO.AddDays(-2)),
                Pedido(221000, "99", "NO RECORDADO, S.L.", CORREO.AddDays(-2))
            };
            var muchas = Recordado("77");
            muchas.Veces = 40;

            AsignacionPedidoCompra a = ReglaOrigenAgencia.Asignar(Vic(), new[] { muchas, Recordado("88") }, pedidos, CORREO);

            CollectionAssert.AreEquivalent(new[] { 220989, 220990 }, a.Candidatos.Select(c => c.Pedido).ToList());
            Assert.IsTrue(a.Candidatos.All(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA_VARIOS));
            Assert.AreEqual("Desde Vic nos mandan por DB Schenker LABORATORIOS EJEMPLO, S.A. y COSMETICA DEL BAGES, S.L.: elija el pedido.", a.Candidatos[0].Explicacion);
            Assert.IsNull(a.ProveedorPropuesto);

            // Aunque solo uno tuviera varios pedidos y control de pendientes, con varios recordados no elige otra regla
            List<PedidoCompraEnAire> soloDeUno = new List<PedidoCompraEnAire>
            {
                Pedido(220980, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-5), control: true),
                Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3), control: true)
            };
            AsignacionPedidoCompra b = ReglaControlPendientes.Completar(ReglaOrigenAgencia.Asignar(Vic(), new[] { muchas, Recordado("88") }, soloDeUno, CORREO), soloDeUno, CORREO, 30);
            Assert.AreEqual(2, b.Candidatos.Count);
        }

        [TestMethod]
        public void Completar_SiOtroCriterioYaDioCandidatos_NoSeToca()
        {
            var porNombre = new AsignacionPedidoCompra
            {
                ProveedorPropuesto = "88",
                Candidatos = new List<CandidatoPedidoCompra> { new CandidatoPedidoCompra { Empresa = "1", Pedido = 220990, Proveedor = "88", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE } }
            };
            List<PedidoCompraEnAire> pedidos = new List<PedidoCompraEnAire> { Pedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3)) };

            Assert.AreSame(porNombre, ReglaOrigenAgencia.Completar(porNombre, Vic(), new[] { Recordado("77") }, pedidos, CORREO));
        }

        [TestMethod]
        public void SeAprendeDeUnCriterioFuerte_NuncaDeSuPropiaRegla()
        {
            var pedido = new CandidatoPedidoCompra { Empresa = "1", Pedido = 220989, Proveedor = "77" };
            CorreoSeguimientoFila Con(string motivo) => new CorreoSeguimientoFila
            {
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 220989, Proveedor = "77", Motivo = motivo }
                })
            };

            Assert.IsTrue(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_NUMERO) }, pedido));
            Assert.IsTrue(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO) }, pedido));
            Assert.IsTrue(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO) }, pedido));
            Assert.IsFalse(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA) }, pedido));
            Assert.IsFalse(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES) }, pedido));
            Assert.IsFalse(ReglaOrigenAgencia.EnsenaAutomatico(new[] { Con(AsignadorPedidosCompra.MOTIVO_NOMBRE) }, pedido));
        }

        #endregion

        #region De punta a punta: el job, la asignación a mano y sin la tabla

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private LecturaBuzonCompras lectura;
        private List<Exception> errores;
        private List<CorreoSeguimientoFila> avisos;
        private DateTime ahora;

        [TestInitialize]
        public void Preparar()
        {
            ServicioCorreosSeguimientoCompras.OlvidarSinTablaOrigenesApuntado();
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            errores = new List<Exception>();
            avisos = new List<CorreoSeguimientoFila>();
            ahora = CORREO.AddMinutes(5);
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(lectura));
            repositorio.AnadirPedido(220989, "77", "LABORATORIOS EJEMPLO, S.A.", CORREO.AddDays(-3), new DateTime(2026, 12, 5));
        }

        private ServicioAplicacionCorreosCompras Aplicacion()
        {
            IAvisadorCorreosPorAsignar avisador = A.Fake<IAvisadorCorreosPorAsignar>();
            A.CallTo(() => avisador.Avisar(A<CorreoSeguimientoFila>._, A<int>._)).ReturnsLazily((CorreoSeguimientoFila f, int n) =>
            {
                avisos.Add(f);
                return Task.CompletedTask;
            });
            return new ServicioAplicacionCorreosCompras(repositorio, () => true, avisador, (empresa, productos, descripcion, causas) => { }, () => ahora, errores.Add, _ => false);
        }

        private async Task Pasada(params CorreoCompras[] correos)
        {
            lectura = new LecturaBuzonCompras { Correos = correos.ToList() };
            _ = await new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false,
                aplicacion: Aplicacion(), diasPedidoDePendientes: () => 30).Pasada(ahora);
        }

        private static CorreoCompras Schenker(string numero, DateTime recibido, string extra = "") => new CorreoCompras
        {
            Id = "g" + numero, MensajeId = "<" + numero + "@ejemplo.test>", Recibido = recibido, RemitenteDireccion = "gestor_aut@schenker.com",
            Asunto = "Nuevo envío DB Schenker/New order DB Schenker",
            Cuerpo = "Apreciado cliente,\nEl remitente\nha generado un nuevo envío para usted con número " + numero + " Puede seguir el estado del envio en el siguiente link:\n"
                + "http://www.dbschenker.es/log-es-es/start/\nIntroduciendo en la casilla de Quick Tracking el número: " + numero + "\n" + extra
        };

        [TestMethod]
        public async Task Job_ConLoAprendido_SeAplicaConElPorQue_YNoSeRetroalimenta()
        {
            repositorio.OrigenesAgencia.Add(Recordado("77"));

            await Pasada(Schenker("ESVIC262013788", CORREO));

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220989, fila.PedidoAplicado);
            StringAssert.StartsWith(repositorio.Rastros.Single().PorQuePedido, "DB Schenker desde Vic: las otras veces era de LABORATORIOS EJEMPLO");
            Assert.AreEqual(1, repositorio.OrigenesAgencia.Single().Veces, "Lo que asigna esta misma regla no enseña");
            string cabecera = TextosReenvioCorreoSeguimiento.Cabecera(fila, new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.FechaCambiada, Pedido = 220989 },
                "gestor_aut@schenker.com", CORREO);
            StringAssert.Contains(cabecera, "DB Schenker desde Vic");
        }

        [TestMethod]
        public async Task SinLaTabla_ComoAntes_YSeApuntaUnaVez()
        {
            repositorio.ConOrigenes = false;
            repositorio.OrigenesAgencia.Add(Recordado("77"));

            await Pasada(Schenker("ESVIC262013788", CORREO));
            await Pasada(Schenker("ESVIC262013799", CORREO.AddMinutes(1)));

            Assert.IsTrue(repositorio.Correos.All(c => !c.PedidoAplicado.HasValue && AsignadorPedidosCompra.Deserializar(c.PedidosCandidatos).Count == 0));
            Assert.AreEqual(1, errores.Count(e => e.Message.Contains("Issue857i")));
        }

        [TestMethod]
        public async Task LaPersonaAsigna_SeAprende_YElSiguienteSaleSolo()
        {
            await Pasada(Schenker("ESVIC262013788", CORREO));
            CorreoSeguimientoFila porAsignar = repositorio.Correos.Single();
            Assert.IsFalse(porAsignar.PedidoAplicado.HasValue);

            ResultadoOperacionCorreoCompra r = await Aplicacion().Asignar(porAsignar.Id, new PeticionAsignarCorreoCompraDTO { Pedido = 220989 }, false, "NUEVAVISION\\compras");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, r.Codigo);
            OrigenAgenciaRecordado aprendido = repositorio.OrigenesAgencia.Single();
            Assert.AreEqual(SCHENKER, aprendido.Agencia);
            Assert.AreEqual("VIC", aprendido.Origen);
            Assert.AreEqual("77", aprendido.Proveedor);
            Assert.AreEqual(1, aprendido.Veces);
            Assert.AreEqual("Vic", r.Respuesta.OrigenAgenciaAprendido);
            StringAssert.Contains(r.Mensaje, "Apuntado que lo que manda DB Schenker desde Vic es del proveedor 77.");

            // El siguiente de Vic, al día siguiente, sale solo (y no suma: lo ha asignado la propia regla)
            ahora = CORREO.AddDays(1).AddMinutes(5);
            await Pasada(Schenker("ESVIC262019999", CORREO.AddDays(1)));
            Assert.AreEqual(220989, repositorio.Correos.Single(c => c.Codigo == "ESVIC262019999").PedidoAplicado);
            Assert.AreEqual(1, repositorio.OrigenesAgencia.Single().Veces);
        }

        [TestMethod]
        public async Task Job_PorNuestroNumero_Ensena()
        {
            await Pasada(Schenker("ESVIC262013788", CORREO, "Su pedido 220989"));

            Assert.AreEqual(220989, repositorio.Correos.Single().PedidoAplicado);
            OrigenAgenciaRecordado aprendido = repositorio.OrigenesAgencia.Single();
            Assert.AreEqual("77", aprendido.Proveedor);
            Assert.AreEqual(ServicioAplicacionCorreosCompras.APLICADO_POR_AUTOMATICO, aprendido.Usuario);
        }

        [TestMethod]
        public async Task Descartar_NiAprendeNiBorra()
        {
            repositorio.OrigenesAgencia.Add(Recordado("88"));
            await Pasada(Schenker("ESVIC262013788", CORREO));

            _ = await Aplicacion().Descartar(repositorio.Correos.Single().Id, "NUEVAVISION\\compras");

            Assert.AreEqual("88", repositorio.OrigenesAgencia.Single().Proveedor);
            Assert.AreEqual(1, repositorio.OrigenesAgencia.Single().Veces);
        }

        #endregion

        #region Retoques del 25/11/26: DHL, recordar el remitente, aviso por asignar y envíos que ya no están pendientes

        private const string DHL_ECOMMERCE = "¡Muy pronto recibirás el envío de MAYSTAR S,L !\nExpedición..: 4313144450\nBultos.....: 8\nKilos.......: 123\nRef.Cliente.: 128013\n" +
            "Siga su envío en https://www.dhl.com/es-es/home/tracking.html?tracking-id=4313144450";

        [TestMethod]
        public void Dhl_SacaElRemitenteDelPaquete_YSuRefClienteNoEsPedidoNuestro()
        {
            var correo = new CorreoCompras
            {
                Id = "d1", MensajeId = "<dhl@ejemplo.test>", Recibido = CORREO, RemitenteDireccion = "noreply@dhl.com",
                Asunto = "Su envío DHL 4313144450 está en camino", Cuerpo = DHL_ECOMMERCE
            };

            AnalisisCorreoSeguimiento a = AnalizadorCorreoSeguimiento.Analizar(correo, AgenciasEntradaRegistradas.Todas, new List<RemitenteMarcado>());

            Assert.AreEqual("maystar s,l", a.RemitentePaquete);
            CollectionAssert.Contains(a.NumerosQueNoSonPedido, "128013");
            var pedidos = new List<PedidoCompraEnAire> { Pedido(128013, "50", "OTRO PROVEEDOR, S.L.", CORREO.AddDays(-2)) };
            Assert.AreEqual(1, AsignadorPedidosCompra.Asignar(correo, a.RemitentesOriginales, pedidos).Candidatos.Count, "Sin decírselo, «Ref.Cliente.: 128013» casaría");
            Assert.AreEqual(0, AsignadorPedidosCompra.Asignar(correo, a.RemitentesOriginales, pedidos, a.RemitentePaquete, null, a.NumerosQueNoSonPedido).Candidatos.Count);
        }

        [TestMethod]
        public void RecordarRemitente_TambienPorOtroCriterio_SiNoEstaYa_YNuncaLoHeredado()
        {
            var pedido = new CandidatoPedidoCompra { Empresa = "1", Pedido = 220983, Proveedor = "65" };
            CorreoSeguimientoFila Fila(string motivo) => new CorreoSeguimientoFila
            {
                RemitentePaquete = "maystar s,l",
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 220983, Proveedor = "65", Motivo = motivo }
                })
            };

            Assert.AreEqual("maystar s,l", ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_NOMBRE) }, pedido));
            Assert.AreEqual("maystar s,l", ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_DOMINIO) }, pedido));
            Assert.AreEqual("maystar s,l", ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_NOMBRE_REMITENTE_PAQUETE) }, pedido));
            Assert.IsNull(ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO) }, pedido), "Heredado de otro correo: puede ser un intermediario");
            Assert.IsNull(ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_NOMBRE) }, pedido,
                new[] { new RemitentePaqueteRecordado { RemitentePaquete = "maystar s,l", Proveedor = "99" } }), "Lo que ya está en la memoria no se pisa");
            Assert.IsNull(ReglaRecordarRemitentePaquete.QueRecordar(new[] { Fila(AsignadorPedidosCompra.MOTIVO_NOMBRE) },
                new CandidatoPedidoCompra { Empresa = "1", Pedido = 1, Proveedor = "70" }), "De otro proveedor no");
        }

        [TestMethod]
        public void Reenvio_PorAsignar_DiceDondeAsignarlo()
        {
            StringAssert.EndsWith(TextosReenvioCorreoSeguimiento.QueSeHaHecho(new ResultadoCorreoGuardado { Desenlace = DesenlaceCorreoSeguimiento.PorAsignar }),
                "Le llegará el aviso en la campana de Nesto. Puede asignarlo ya en Nesto: Productos › Envíos por asignar.");
        }

        [TestMethod]
        public async Task AvisoPorAsignar_CuentaDesdeQueSeRecibio_YSiNoHaPasadoDiceCuandoToca()
        {
            // Recibido a las 14:11 y guardado a las 14:30: aún no han pasado 20 min; toca a las 14:31 (no en la pasada de las 15:00)
            DateTime recibido = new DateTime(2026, 11, 25, 14, 11, 0);
            ahora = new DateTime(2026, 11, 25, 14, 30, 0);
            await Pasada(Schenker("ESVIC262013788", recibido));
            Assert.AreEqual(0, avisos.Count);

            ServicioAplicacionCorreosCompras aplicacion = Aplicacion();
            ahora = new DateTime(2026, 11, 25, 14, 30, 0);
            _ = await aplicacion.AvisarPendientes();
            Assert.AreEqual(recibido.AddMinutes(20), aplicacion.ProximoAvisoPendiente);
            Assert.AreEqual(TimeSpan.FromSeconds(90), CorreosSeguimientoComprasJobsService.EsperaAvisoSuelto(aplicacion.ProximoAvisoPendiente, ahora));

            ahora = new DateTime(2026, 11, 25, 14, 31, 30);
            Assert.AreEqual(1, await aplicacion.AvisarPendientes());
            Assert.IsNull(aplicacion.ProximoAvisoPendiente);
        }

        [TestMethod]
        public void EsperaAvisoSuelto_SoloSiTocaAntesDeLaSiguientePasada()
        {
            DateTime ahora = new DateTime(2026, 11, 25, 14, 30, 0);
            Assert.IsNull(CorreosSeguimientoComprasJobsService.EsperaAvisoSuelto(null, ahora));
            Assert.IsNull(CorreosSeguimientoComprasJobsService.EsperaAvisoSuelto(ahora.AddMinutes(-1), ahora));
            Assert.IsNull(CorreosSeguimientoComprasJobsService.EsperaAvisoSuelto(ahora.AddMinutes(40), ahora), "Ya lo hará la pasada");
        }

        [TestMethod]
        public async Task AvisoPorAsignar_NoAvisaDeUnEnvioEntregadoNiSinCandidatosEnElAire()
        {
            ahora = CORREO.AddMinutes(30);
            var entregado = new CorreoSeguimientoFila
            {
                MensajeId = "<e>", Recibido = CORREO, FechaCaptura = CORREO, Agencia = SCHENKER, Codigo = "ESMNR264773770", EstadoEnvio = "Entregado"
            };
            _ = await repositorio.Insertar(entregado);
            _ = await repositorio.Insertar(new CorreoSeguimientoFila
            {
                MensajeId = "<r>", Recibido = CORREO, FechaCaptura = CORREO, Agencia = "DHL", Codigo = "4313144450",
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 111111, Proveedor = "65", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE },
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 111112, Proveedor = "65", Motivo = AsignadorPedidosCompra.MOTIVO_NOMBRE }
                })
            });

            Assert.AreEqual(0, await Aplicacion().AvisarPendientes());
        }

        private static CorreoSeguimientoFila Estado(int id, DateTime recibido, string estado = null, int? pedidoReal = null) => new CorreoSeguimientoFila
        {
            Id = id, MensajeId = "<" + id + ">", Recibido = recibido, Agencia = SCHENKER, Codigo = "ESMNR264773770", EstadoEnvio = estado, PedidoReal = pedidoReal,
            PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
            {
                new CandidatoPedidoCompra { Empresa = "1", Pedido = 220923, Proveedor = "40", Motivo = AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO }
            })
        };

        [TestMethod]
        public void EnvioYaNoPendiente_CasoDsv_LosCorreosDeEstadoDeUnEnvioYaAplicado()
        {
            DateTime dia = new DateTime(2026, 11, 18, 10, 0, 0);
            var aviso = new CorreoSeguimientoFila
            {
                Id = 18, MensajeId = "<18>", Recibido = dia, Agencia = SCHENKER, Codigo = "ESMNR264773770", EmpresaAplicada = "1", PedidoAplicado = 220923,
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 220923, Proveedor = "40", Motivo = AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO }
                })
            };
            var correos = new List<CorreoSeguimientoFila> { aviso, Estado(19, dia.AddHours(20)), Estado(20, dia.AddHours(26)), Estado(21, dia.AddHours(30), "Entregado"), Estado(22, dia.AddHours(31), "Entregado") };

            DateTime hoy = new DateTime(2026, 11, 25);
            Assert.AreEqual(0, ComprasAgenciasEntradaController.ComponerPorAsignar(correos, new List<PedidoCompraEnAire>(), hoy.AddDays(-7), hoy).Envios.Count);
            EnvioPorAsignarDTO llegado = ComprasAgenciasEntradaController.ComponerPorAsignar(correos, new List<PedidoCompraEnAire>(), hoy.AddDays(-7), hoy, incluirLlegados: true).Envios.Single();
            Assert.AreEqual("El envío ya está asignado al pedido de compra 220923.", llegado.MotivoYaNoPendiente);
            Assert.AreEqual(4, llegado.Correos.Count);
        }

        [TestMethod]
        public void EnvioYaNoPendiente_CadaCaso()
        {
            DateTime dia = new DateTime(2026, 11, 20, 10, 0, 0);
            var enAire = new List<PedidoCompraEnAire> { Pedido(220923, "40", "DDUUEETT, S.L.", dia.AddDays(-5)) };

            Assert.IsNull(ReglaEnvioYaNoPendiente.Motivo(new[] { Estado(1, dia) }, enAire), "Con su candidato en el aire sigue pendiente");
            Assert.AreEqual("La agencia dice que el envío ya está entregado.", ReglaEnvioYaNoPendiente.Motivo(new[] { Estado(1, dia), Estado(2, dia.AddHours(5), "Entregado") }, enAire));
            Assert.AreEqual("El pedido de compra 220923 ya se ha recibido.", ReglaEnvioYaNoPendiente.Motivo(new[] { Estado(1, dia, pedidoReal: 220923) }, enAire));
            Assert.AreEqual("El pedido de compra 220923 ya no tiene nada pendiente de recibir.", ReglaEnvioYaNoPendiente.Motivo(new[] { Estado(1, dia) }, new List<PedidoCompraEnAire>()));
            Assert.IsNull(ReglaEnvioYaNoPendiente.Motivo(new[] { Estado(1, dia) }, null), "Sin saber qué hay en el aire, no se descarta");
            var descartado = Estado(1, dia);
            descartado.Descartado = true;
            Assert.AreEqual("El envío ya está descartado: no es de ninguna compra.", ReglaEnvioYaNoPendiente.Motivo(new[] { descartado, Estado(2, dia.AddHours(1)) }, enAire));
            var sinCandidatos = new CorreoSeguimientoFila { Id = 3, Recibido = dia, Agencia = SCHENKER, Codigo = "ESVIC262013788" };
            Assert.IsNull(ReglaEnvioYaNoPendiente.Motivo(new[] { sinCandidatos }, new List<PedidoCompraEnAire>()), "Sin candidatos sigue saliendo");
        }

        #endregion
    }
}
