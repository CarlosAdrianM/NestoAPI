using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosCliente;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// Carlos (19/11/26, caso del cliente 33895): la señal de un evento se cobra en Cajas (efectivo y tarjeta en la tienda,
    /// transferencia en Administración) y queda todo como con el enlace pagado; «Marcar pagada» solo Administración; aviso al
    /// vender a mano el curso en un pedido normal.
    /// </summary>
    [TestClass]
    public class CobroSenalCajaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 19, 11, 0, 0);
        private const int EVENTO = 7;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<EventoSenal> senales;
        private List<PagoTPV> pagosTPV;
        private List<PropuestaSenalEvento> propuestas;
        private IConfirmadorSenalEvento confirmador;
        private IRepositorioCobroSenalCaja repositorio;
        private HashSet<string> conCobro;
        private List<PreContabilidad> lineasRegistradas;
        private List<int> enlacesAnulados;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");
        private static readonly IPrincipal AIDA = Usuario("NUEVAVISION\\Aida", "Administración");
        private static readonly IPrincipal CARLOS = Usuario("NUEVAVISION\\Carlos", "Dirección");
        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Vendedores");
        private static readonly IPrincipal CLIENTA = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "33895") }, "prueba"));

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO, Empresa = "1", Titulo = "Masterclass Rosácea", Fecha = new DateTime(2026, 11, 24), ImporteSenal = 100,
                    Activo = true, Almacen = "REI", ProductoCurso = "90077", Usuario = "Reina", FechaModificacion = HOY },
                new Evento { Id = 8, Empresa = "1", Titulo = "Curso pasado", Fecha = new DateTime(2026, 11, 10), ImporteSenal = 50,
                    Activo = true, Almacen = "REI", ProductoCurso = "90078", Usuario = "Reina", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            senales = new List<EventoSenal>();
            pagosTPV = new List<PagoTPV>();
            propuestas = new List<PropuestaSenalEvento>();
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.EventosSenales).Returns(Crear(senales));
            A.CallTo(() => db.PagosTPV).Returns(Crear(pagosTPV));

            confirmador = A.Fake<IConfirmadorSenalEvento>();
            A.CallTo(() => confirmador.MotivoNoFacturaSolo(A<EventoInscripcion>._, A<Evento>._, A<FacturacionCursoInscripcion>._)).Returns(null);

            conCobro = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lineasRegistradas = null;
            enlacesAnulados = null;
            repositorio = A.Fake<IRepositorioCobroSenalCaja>();
            A.CallTo(() => repositorio.DocumentosConCobro(A<string>._, A<string>._, A<IEnumerable<string>>._))
                .ReturnsLazily((string e, string c, IEnumerable<string> docs) => Task.FromResult(new HashSet<string>(docs.Where(conCobro.Contains), StringComparer.OrdinalIgnoreCase)));
            // Como el de verdad: si ya hay cobro no contabiliza; siempre anota la pagada dentro de la transacción
            A.CallTo(() => repositorio.Registrar(A<int>._, A<string>._, A<string>._, A<string>._, A<List<PreContabilidad>>._,
                    A<IReadOnlyCollection<int>>._, A<string>._, A<string>._, A<Func<Task>>._))
                .ReturnsLazily(async call =>
                {
                    string documento = call.GetArgument<string>(3);
                    bool yaEstaba = conCobro.Contains(documento);
                    if (!yaEstaba)
                    {
                        lineasRegistradas = call.GetArgument<List<PreContabilidad>>(4);
                        enlacesAnulados = call.GetArgument<IReadOnlyCollection<int>>(5)?.ToList();
                        conCobro.Add(documento);
                    }
                    await call.GetArgument<Func<Task>>(8)().ConfigureAwait(false);
                    return new RegistroCobroSenalCaja { Asiento = yaEstaba ? 0 : 4711, CobroYaEstaba = yaEstaba };
                });
        }

        private ServicioInscripcionesEventos Servicio() =>
            new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => u == "Reina" ? "REI" : null, confirmador)
            {
                RepositorioCobroCaja = repositorio,
                PropuestasSenalPendientes = (e, c) => Task.FromResult(propuestas.Where(p => p.Cliente == c).ToList()),
                ParametroUsuario = (empresa, usuario, clave) =>
                    clave == "CajaDefecto" ? "57000005" : clave == "CuentaBancoTarjeta" ? "57200013" : clave == "DiarioCaja" ? "_CajaReina"
                    : clave == "DelegaciónDefecto" ? "REI" : clave == "FormaVentaDefecto" ? "TIE" : null
            };

        private EventoInscripcion Reserva(int id, EstadoInscripcionEvento estado = EstadoInscripcionEvento.Reservada, int evento = EVENTO,
            string modalidad = null, int? pago = null)
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = evento, Empresa = "1  ", Cliente = "33895", Contacto = "0  ", Origen = "Nesto", Estado = estado.ToString(),
                FechaLimitePago = HOY.AddDays(3).Date, Modalidad = modalidad, PagoTPVId = pago, UsuarioCreacion = "Reina", FechaCreacion = HOY.AddDays(-2),
                Usuario = "Reina", FechaModificacion = HOY.AddDays(-2)
            };
            inscripciones.Add(i);
            return i;
        }

        private static CobrarSenalCajaDTO Cobro(string forma = "EFC", decimal importe = 100M) => new CobrarSenalCajaDTO
        {
            FormaPago = forma,
            Importe = importe,
            Fecha = HOY.Date
        };

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Reglas_FormaPago_LasDeCajasYLasDeLaInscripcion()
        {
            Assert.AreEqual(FormaPagoInscripcionEvento.Efectivo, ReglasCobroSenalCaja.FormaPago("EFC"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ReglasCobroSenalCaja.FormaPago("tar"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Transferencia, ReglasCobroSenalCaja.FormaPago("TRN"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Efectivo, ReglasCobroSenalCaja.FormaPago("Efectivo"));
            Assert.IsNull(ReglasCobroSenalCaja.FormaPago("RCB"), "Un recibo no es un cobro de caja");
            Assert.IsNull(ReglasCobroSenalCaja.FormaPago(null));
            Assert.AreEqual("EFC", ReglasCobroSenalCaja.CodigoFormaPago(FormaPagoInscripcionEvento.Efectivo));
            Assert.AreEqual("TAR", ReglasCobroSenalCaja.CodigoFormaPago(FormaPagoInscripcionEvento.Tarjeta));
            Assert.AreEqual("TRN", ReglasCobroSenalCaja.CodigoFormaPago(FormaPagoInscripcionEvento.Transferencia));
        }

        [TestMethod]
        public void Reglas_ElDiaDelEventoValeEntero_AunquePaseLaHora()
        {
            // Carlos 19/11/26: compara por fecha, no por fecha y hora: un evento de hoy a las 10:00 se cobra en Cajas a las 20:00
            var deHoyPorLaManana = new Evento { Id = 10, Titulo = "Masterclass de hoy", Fecha = HOY.Date.AddHours(10), ImporteSenal = 100 };
            DateTime estaTarde = HOY.Date.AddHours(20);

            Assert.IsTrue(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }, deHoyPorLaManana, estaTarde));
            Assert.IsTrue(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }, deHoyPorLaManana, HOY.Date.AddHours(23).AddMinutes(59)));
            Assert.IsFalse(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }, deHoyPorLaManana, HOY.Date.AddDays(1).AddMinutes(1)),
                "Al día siguiente ya no");
        }

        [TestMethod]
        public void Reglas_Importe_LaSenalEnteraONada()
        {
            Assert.IsNull(ReglasCobroSenalCaja.MotivoImporte(100M, 100M, eventos[0]));
            Assert.IsNull(ReglasCobroSenalCaja.MotivoImporte(100.004M, 100M, eventos[0]), "Redondeo a céntimos");
            string motivo = ReglasCobroSenalCaja.MotivoImporte(50M, 100M, eventos[0]);
            StringAssert.Contains(motivo, "se cobra entera o no se cobra");
            StringAssert.Contains(motivo, "100,00");
            Assert.IsNotNull(ReglasCobroSenalCaja.MotivoImporte(120M, 100M, eventos[0]), "Tampoco de más");
        }

        [TestMethod]
        public void Reglas_ReservaSinPagar_ReservadaOCaducadaDePagoYDeHoyEnAdelante()
        {
            Assert.IsTrue(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }, eventos[0], HOY));
            Assert.IsTrue(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Caducada" }, eventos[0], HOY), "Caducada puede pasar a Pagada");
            Assert.IsFalse(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Pagada" }, eventos[0], HOY));
            Assert.IsFalse(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Anulada" }, eventos[0], HOY));
            Assert.IsFalse(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada", Modalidad = "Gratuita" }, eventos[0], HOY));
            Assert.IsFalse(ReglasCobroSenalCaja.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }, eventos[1], HOY), "Evento pasado");
            Assert.AreEqual("Señal Masterclass Rosácea (24/11)", ReglasCobroSenalCaja.Descripcion(eventos[0], ModalidadInscripcionEvento.Reembolsable));
        }

        [TestMethod]
        public void Reglas_MotivoNoCobrable_EnOrden()
        {
            var i = new EventoInscripcion { Id = 1 };
            StringAssert.Contains(ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], true, true, true, true, 5, "x"), "transferencia");
            StringAssert.Contains(ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], false, true, true, true, 5, "x"), "propuesta");
            StringAssert.Contains(ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], false, false, true, true, 5, "x"), "enlace");
            StringAssert.Contains(ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], false, false, false, true, 5, "x"), "pedido de cursos 5");
            Assert.AreEqual("x", ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], false, false, false, false, null, "x"));
            Assert.IsNull(ReglasCobroSenalCaja.MotivoNoCobrable(i, eventos[0], false, false, false, false, null, ""));
            StringAssert.Contains(ReglasCobroSenalCaja.MotivoNoCobrable(new EventoInscripcion { EventoSenalId = 3 }, eventos[0], false, false, false, false, null, null),
                "transferencia", "Con la señal de #591 en la inscripción");
        }

        [TestMethod]
        public void Apuntes_LosDeUnCobroACuentaDeCajasConElDocumentoDelEnlace()
        {
            var i = new EventoInscripcion { Id = 123, Empresa = "1  ", Cliente = "33895 ", Contacto = "0  " };
            var caja = new CajaCobroSenal { Cuenta = "57000005", Diario = "_CajaReina", Delegacion = "REI", FormaVenta = "TIE", AQuien = "Reina" };

            List<PreContabilidad> lineas = ConstructorApuntesCobroSenalCaja.Construir(i, eventos[0], 100M, FormaPagoInscripcionEvento.Efectivo,
                HOY, caja, "ticket 7", "NUEVAVISION\\Reina", HOY);

            Assert.AreEqual(2, lineas.Count);
            PreContabilidad cliente = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE);
            PreContabilidad contrapartida = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual("33895", cliente.Nº_Cuenta);
            Assert.AreEqual("0", cliente.Contacto);
            Assert.AreEqual(100M, cliente.Haber);
            Assert.AreEqual(0M, cliente.Debe);
            Assert.IsNull(cliente.Liquidado, "A cuenta: lo liquida la facturación del curso contra su factura");
            Assert.AreEqual("INS123", cliente.Nº_Documento);
            Assert.AreEqual(Constantes.TiposExtractoCliente.PAGO, cliente.TipoApunte);
            Assert.AreEqual("57000005", contrapartida.Nº_Cuenta);
            Assert.AreEqual(100M, contrapartida.Debe);
            Assert.AreEqual("INS123", contrapartida.Nº_Documento);
            foreach (PreContabilidad l in lineas)
            {
                Assert.AreEqual("1", l.Empresa);
                Assert.AreEqual("_CajaReina", l.Diario);
                Assert.AreEqual("REI", l.Delegación);
                Assert.AreEqual("TIE", l.FormaVenta);
                Assert.AreEqual("EFC", l.FormaPago);
                Assert.AreEqual(HOY.Date, l.Fecha);
                Assert.IsTrue(l.Concepto.Length <= 50, l.Concepto);
                StringAssert.Contains(l.Concepto, "a Reina");
            }
            StringAssert.Contains(cliente.Concepto, "INS123");
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento cuadra");
        }

        [TestMethod]
        public void Permisos_CobrarEnCaja_TiendasEfectivoYTarjeta_AdministracionTodo()
        {
            Assert.IsTrue(PermisosEventos.PuedeCobrarSenalEnCaja(REINA, FormaPagoInscripcionEvento.Efectivo));
            Assert.IsTrue(PermisosEventos.PuedeCobrarSenalEnCaja(REINA, FormaPagoInscripcionEvento.Tarjeta));
            Assert.IsFalse(PermisosEventos.PuedeCobrarSenalEnCaja(REINA, FormaPagoInscripcionEvento.Transferencia));
            Assert.IsTrue(PermisosEventos.PuedeCobrarSenalEnCaja(AIDA, FormaPagoInscripcionEvento.Transferencia));
            Assert.IsTrue(PermisosEventos.PuedeCobrarSenalEnCaja(CARLOS, FormaPagoInscripcionEvento.Transferencia));
            Assert.IsFalse(PermisosEventos.PuedeCobrarSenalEnCaja(VENDEDOR));
            Assert.IsFalse(PermisosEventos.PuedeCobrarSenalEnCaja(CLIENTA));
        }

        [TestMethod]
        public void Permisos_MarcarPagadaAMano_SoloAdministracionYDireccion()
        {
            Assert.IsFalse(PermisosEventos.PuedeMarcarPagadaAMano(REINA));
            Assert.IsFalse(PermisosEventos.PuedeMarcarPagadaAMano(Usuario("NUEVAVISION\\Laura", "TiendaOnline")));
            Assert.IsFalse(PermisosEventos.PuedeMarcarPagadaAMano(VENDEDOR));
            Assert.IsTrue(PermisosEventos.PuedeMarcarPagadaAMano(AIDA));
            Assert.IsTrue(PermisosEventos.PuedeMarcarPagadaAMano(CARLOS));
            Assert.IsTrue(PermisosEventos.PuedeMarcarPagadaAMano(Usuario("NUEVAVISION\\Info", "Informática")));
        }

        // ---------------- Marcar pagada: solo Administración ----------------

        [TestMethod]
        public async Task MarcarPagada_Tienda_403QueDiceQueSeCobraEnCajas()
        {
            Reserva(1);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().MarcarPagada(1, new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, REINA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            StringAssert.Contains(ex.Message, "Cajas");
            Assert.AreEqual("Reservada", inscripciones[0].Estado);
        }

        [TestMethod]
        public async Task MarcarPagada_Administracion_SigueFuncionando()
        {
            Reserva(1);

            InscripcionEventoDTO dto = await Servicio().MarcarPagada(1, new MarcarPagoInscripcionDTO { FormaPago = "Transferencia" }, AIDA);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
        }

        [TestMethod]
        public async Task InscribirYaPagada_Tienda_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO, new InscribirEventoDTO
            {
                Empresa = "1", Cliente = "33895", Origen = "Nesto", Pago = new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }
            }, REINA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // ---------------- Reservas sin pagar ----------------

        [TestMethod]
        public async Task Reservas_LasDelClienteSinPagarYDeHoyEnAdelante()
        {
            Reserva(1);
            Reserva(2, EstadoInscripcionEvento.Caducada);
            Reserva(3, EstadoInscripcionEvento.Pagada);
            Reserva(4, evento: 8); // evento pasado
            Reserva(5, modalidad: "Gratuita");

            List<ReservaEventoSinPagarDTO> reservas = await Servicio().LeerReservasSinPagar("1", "33895", REINA);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, reservas.Select(r => r.InscripcionId).ToList());
            ReservaEventoSinPagarDTO r1 = reservas.Single(r => r.InscripcionId == 1);
            Assert.IsTrue(r1.Cobrable);
            Assert.IsNull(r1.Motivo);
            Assert.AreEqual(100M, r1.Importe);
            Assert.AreEqual("INS1", r1.Documento);
            Assert.AreEqual("Señal Masterclass Rosácea (24/11)", r1.Descripcion);
            Assert.AreEqual("0", r1.Contacto);
            Assert.IsFalse(r1.CobroYaApuntado);
        }

        [TestMethod]
        public async Task Reservas_ConSenalPorTransferenciaOPropuesta_NoCobrables()
        {
            Reserva(1);
            senales.Add(new EventoSenal { Id = 40, EventoId = EVENTO, Empresa = "1", Cliente = "33895", Contacto = "0", NumOrdenExtracto = 8001, Importe = 100 });

            ReservaEventoSinPagarDTO r = (await Servicio().LeerReservasSinPagar("1", "33895", REINA)).Single();
            Assert.IsFalse(r.Cobrable);
            StringAssert.Contains(r.Motivo, "transferencia");

            senales.Clear();
            propuestas.Add(new PropuestaSenalEvento { Id = 1, EventoId = EVENTO, Cliente = "33895", Contacto = "0", InscripcionId = 1, Estado = "Propuesta" });
            r = (await Servicio().LeerReservasSinPagar("1", "33895", REINA)).Single();
            Assert.IsFalse(r.Cobrable);
            StringAssert.Contains(r.Motivo, "propuesta");
        }

        [TestMethod]
        public async Task Reservas_ConElEnlaceYaCobrado_NoCobrable()
        {
            Reserva(1, pago: 555);
            pagosTPV.Add(new PagoTPV { Id = 555, Estado = Constantes.EstadosPagoTPV.AUTORIZADO });

            ReservaEventoSinPagarDTO r = (await Servicio().LeerReservasSinPagar("1", "33895", REINA)).Single();

            Assert.IsFalse(r.Cobrable);
            StringAssert.Contains(r.Motivo, "enlace");
        }

        [TestMethod]
        public async Task Reservas_SiElCursoNoSeFacturaSolo_NoCobrableConElMotivo()
        {
            Reserva(1);
            A.CallTo(() => confirmador.MotivoNoFacturaSolo(A<EventoInscripcion>._, A<Evento>._, A<FacturacionCursoInscripcion>._))
                .Returns("La facturación automática de los cursos está apagada.");

            ReservaEventoSinPagarDTO r = (await Servicio().LeerReservasSinPagar("1", "33895", REINA)).Single();

            Assert.IsFalse(r.Cobrable);
            StringAssert.Contains(r.Motivo, "apagada");
        }

        [TestMethod]
        public async Task Reservas_VendedorNoLasVe_YLaClienta403()
        {
            Reserva(1);

            Assert.AreEqual(0, (await Servicio().LeerReservasSinPagar("1", "33895", VENDEDOR)).Count);
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().LeerReservasSinPagar("1", "33895", CLIENTA));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // ---------------- Cobrar ----------------

        [TestMethod]
        public async Task Cobrar_EfectivoEnLaTienda_CobroYPagadaEnLaMismaTransaccion_YDespuesElCorte2()
        {
            EventoInscripcion i = Reserva(1);
            bool pagadaAntesDelCorte2 = false;
            A.CallTo(() => confirmador.Procesar(A<EventoInscripcion>._, A<Evento>._))
                .Invokes((EventoInscripcion ins, Evento ev) => pagadaAntesDelCorte2 = ins.Estado == "Pagada" && conCobro.Contains("INS1"))
                .Returns(Task.CompletedTask);

            ResultadoCobroSenalCajaDTO r = await Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA);

            Assert.AreEqual("Pagada", i.Estado);
            Assert.AreEqual("Efectivo", i.FormaPago);
            Assert.AreEqual(100M, i.ImportePagado);
            Assert.AreEqual("NUEVAVISION\\Reina", i.UsuarioPago);
            Assert.AreEqual(4711, r.Asiento);
            Assert.IsFalse(r.CobroYaEstaba);
            Assert.AreEqual("INS1", r.Documento);
            Assert.IsTrue(pagadaAntesDelCorte2, "El corte 2 (factura, liquidación, cupón) va con la pagada y el cobro ya guardados");
            A.CallTo(() => confirmador.Procesar(i, A<Evento>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("57000005", lineasRegistradas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE).Nº_Cuenta,
                "En efectivo, la caja de la tienda");
            Assert.AreEqual("_CajaReina", lineasRegistradas[0].Diario);
            StringAssert.Contains(r.Mensaje, "Cobrada la señal");
            StringAssert.Contains(r.Mensaje, "asiento 4711");
        }

        [TestMethod]
        public async Task Cobrar_TarjetaEnLaTienda_ContraElBancoDelDatafonoYAnulaElEnlacePendiente()
        {
            Reserva(1, pago: 555);
            pagosTPV.Add(new PagoTPV { Id = 555, Estado = Constantes.EstadosPagoTPV.PENDIENTE });
            pagosTPV.Add(new PagoTPV { Id = 556, PagoOriginalId = 555, Estado = Constantes.EstadosPagoTPV.PENDIENTE });

            await Servicio().CobrarSenalEnCaja(1, Cobro("TAR"), REINA);

            Assert.AreEqual("57200013", lineasRegistradas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE).Nº_Cuenta);
            Assert.IsTrue(lineasRegistradas.All(l => l.FormaPago == "TAR"));
            CollectionAssert.AreEquivalent(new[] { 555, 556 }, enlacesAnulados, "Que la clienta no pague otra vez con el enlace");
        }

        [TestMethod]
        public async Task Cobrar_TransferenciaAdministracion_DiarioDeAdministracionYSinAQuien()
        {
            Reserva(1);
            var peticion = Cobro("TRN");
            peticion.CuentaCobro = "57200001";

            await Servicio().CobrarSenalEnCaja(1, peticion, AIDA);

            Assert.AreEqual("57200001", lineasRegistradas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE).Nº_Cuenta);
            Assert.IsTrue(lineasRegistradas.All(l => l.Diario == ReglasCobroSenalCaja.DIARIO_CAJA_ADMINISTRACION));
            Assert.IsFalse(lineasRegistradas.Any(l => l.Concepto.Contains(" a Aida")), "Como en Cajas: Administración no lleva «a …»");
            Assert.AreEqual("Transferencia", inscripciones[0].FormaPago);
        }

        [TestMethod]
        public async Task Cobrar_TransferenciaEnLaTienda_403SinTocarNada()
        {
            Reserva(1);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("TRN"), REINA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            StringAssert.Contains(ex.Message, "Administración");
            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_ImporteDistintoDeLaSenal_400SinTocarNada()
        {
            Reserva(1);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("EFC", 50M), REINA));

            Assert.AreNotEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreNotEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "entera");
            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_FormaDePagoQueNoEsDeCaja_400()
        {
            Reserva(1);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("RCB"), AIDA));

            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_ConSenalDe591_409SinCobrarDosVeces()
        {
            Reserva(1);
            senales.Add(new EventoSenal { Id = 40, EventoId = EVENTO, Empresa = "1", Cliente = "33895", Contacto = "0", NumOrdenExtracto = 8001, Importe = 100 });

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("TRN"), AIDA));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_ConLaFacturacionApagada_409SinCobrar()
        {
            Reserva(1);
            A.CallTo(() => confirmador.MotivoNoFacturaSolo(A<EventoInscripcion>._, A<Evento>._, A<FacturacionCursoInscripcion>._))
                .Returns("La facturación automática de los cursos está apagada.");

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA));

            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_SiFallaLaContabilidad_NiCobroNiPagadaNiFactura_YSePuedeRelanzar()
        {
            EventoInscripcion i = Reserva(1);
            A.CallTo(() => repositorio.Registrar(A<int>._, A<string>._, A<string>._, A<string>._, A<List<PreContabilidad>>._,
                    A<IReadOnlyCollection<int>>._, A<string>._, A<string>._, A<Func<Task>>._))
                .Throws(new Exception("Error al contabilizar el diario: fecha no permitida")).Once();

            await Assert.ThrowsExceptionAsync<Exception>(() => Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA));
            Assert.AreEqual("Reservada", i.Estado, "La transacción no ha llegado a anotar la pagada");
            A.CallTo(() => confirmador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();

            ResultadoCobroSenalCajaDTO r = await Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA);

            Assert.AreEqual("Pagada", i.Estado);
            Assert.AreEqual(4711, r.Asiento);
            A.CallTo(() => confirmador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cobrar_ConElCobroYaApuntadoDeUnIntentoCortado_NoVuelveACobrar()
        {
            EventoInscripcion i = Reserva(1);
            conCobro.Add("INS1");

            Assert.IsTrue((await Servicio().LeerReservasSinPagar("1", "33895", REINA)).Single().CobroYaApuntado);
            ResultadoCobroSenalCajaDTO r = await Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA);

            Assert.IsTrue(r.CobroYaEstaba);
            Assert.AreEqual(0, r.Asiento);
            Assert.IsNull(lineasRegistradas, "No se contabiliza otra vez");
            Assert.AreEqual("Pagada", i.Estado);
            StringAssert.Contains(r.Mensaje, "no se ha vuelto a cobrar");
        }

        [TestMethod]
        public async Task Cobrar_DosVeces_LaSegundaTerminaSinCobrar()
        {
            EventoInscripcion i = Reserva(1);
            await Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA);
            lineasRegistradas = null;

            ResultadoCobroSenalCajaDTO r = await Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA);

            Assert.IsTrue(r.CobroYaEstaba);
            Assert.IsNull(lineasRegistradas);
            A.CallTo(() => repositorio.Registrar(A<int>._, A<string>._, A<string>._, A<string>._, A<List<PreContabilidad>>._,
                A<IReadOnlyCollection<int>>._, A<string>._, A<string>._, A<Func<Task>>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => confirmador.ConfirmarSenal(i, A<Evento>._, false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cobrar_PagadaPorOtroCamino_409()
        {
            Reserva(1, EstadoInscripcionEvento.Pagada);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), REINA));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            AssertNadaHecho();
        }

        [TestMethod]
        public async Task Cobrar_VendedorOClienta_403()
        {
            Reserva(1);

            Assert.AreEqual(HttpStatusCode.Forbidden, (await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), VENDEDOR))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CobrarSenalEnCaja(1, Cobro("EFC"), CLIENTA))).StatusCode);
            AssertNadaHecho();
        }

        private void AssertNadaHecho()
        {
            A.CallTo(() => repositorio.Registrar(A<int>._, A<string>._, A<string>._, A<string>._, A<List<PreContabilidad>>._,
                A<IReadOnlyCollection<int>>._, A<string>._, A<string>._, A<Func<Task>>._)).MustNotHaveHappened();
            A.CallTo(() => confirmador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        // ---------------- La facturación: liquidar el cobro de la caja y si se factura sola ----------------

        [TestMethod]
        public async Task Liquidador_CobradaEnCajaEnEfectivo_LiquidaElCobroINSContraLaFactura()
        {
            var extracto = new List<ExtractoCliente>
            {
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9001, Número = "33895", Contacto = "0", TipoApunte = "3", Nº_Documento = "INS1", Importe = -100, ImportePdte = -100 },
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9002, Número = "33895", Contacto = "0", TipoApunte = "2", Nº_Documento = "CV2600900", Importe = 100, ImportePdte = 100 }
            };
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            A.CallTo(() => liquidar.Liquidar("1", 9001, 9002, A<string>._)).Returns(Task.FromResult(new ResultadoLiquidacionEfectos { Exito = true }));
            var i = new EventoInscripcion { Id = 1, Empresa = "1", Cliente = "33895", Contacto = "0", FormaPago = "Efectivo" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "1", "CV2600900", "NUEVAVISION\\Reina");

            Assert.AreEqual(EstadosLiquidacionCurso.LIQUIDADA, r.Estado);
            Assert.AreEqual(9001, r.NumOrdenCobro);
            Assert.AreEqual(9002, r.NumOrdenEfecto);
        }

        [TestMethod]
        public void Confirmador_MotivoNoFacturaSolo()
        {
            var i = new EventoInscripcion { Id = 1 };
            ConfirmadorSenalEvento Confirmador(bool encendido) => new ConfirmadorSenalEvento(db, () => encendido, A.Fake<IRepositorioFacturacionCursos>(),
                A.Fake<ICreadorPedidoCurso>(), A.Fake<IFacturadorPedidoCurso>(), A.Fake<ILiquidadorSenalCurso>(), A.Fake<IAvisadorFacturacionCurso>(),
                null, () => HOY, "SenalesEventos");

            Assert.IsNull(Confirmador(true).MotivoNoFacturaSolo(i, eventos[0], null));
            StringAssert.Contains(Confirmador(false).MotivoNoFacturaSolo(i, eventos[0], null), "apagada");
            var sinCurso = new Evento { Id = 9, Titulo = "Sin curso", Fecha = HOY.AddDays(5), ImporteSenal = 100, Almacen = "REI" };
            StringAssert.Contains(Confirmador(true).MotivoNoFacturaSolo(i, sinCurso, null), "referencia del curso");
        }

        // ---------------- Aviso del curso vendido a mano ----------------

        private static readonly List<AvisoCursoVendidoAMano.EventoConCurso> VIVOS = new List<AvisoCursoVendidoAMano.EventoConCurso>
        {
            new AvisoCursoVendidoAMano.EventoConCurso { Id = 7, Titulo = "Masterclass Rosácea", Fecha = new DateTime(2026, 11, 24), ProductoCurso = "90077" },
            new AvisoCursoVendidoAMano.EventoConCurso { Id = 123, Titulo = "Curso PDRN", Fecha = new DateTime(2026, 12, 1), ProductoCurso = null }
        };

        private static LineaPedidoVentaDTO Linea(string producto, int? eventoId = null) =>
            new LineaPedidoVentaDTO { Producto = producto, tipoLinea = 1, Cantidad = 1, EventoId = eventoId };

        [TestMethod]
        public void AvisoCurso_LaReferenciaDelCursoDeUnEventoVivo_Avisa()
        {
            AvisoPedidoDTO aviso = AvisoCursoVendidoAMano.Aviso(new[] { Linea("41000"), Linea("90077 ") }, VIVOS);

            Assert.IsNotNull(aviso);
            Assert.AreEqual(AvisoCursoVendidoAMano.TIPO, aviso.Tipo);
            StringAssert.Contains(aviso.Mensaje, "La referencia 90077 es el curso del evento «Masterclass Rosácea»");
            StringAssert.Contains(aviso.Mensaje, "inscríbala en el evento y cobre la señal en Cajas");
        }

        [TestMethod]
        public void AvisoCurso_LaReferenciaPropiaDelEvento_Avisa()
        {
            AvisoPedidoDTO aviso = AvisoCursoVendidoAMano.Aviso(new[] { Linea("EVENTO00123NR") }, VIVOS);

            StringAssert.Contains(aviso?.Mensaje, "Curso PDRN");
        }

        [TestMethod]
        public void AvisoCurso_DeLaPlantillaDeCursosOSinCurso_NoAvisa()
        {
            Assert.IsNull(AvisoCursoVendidoAMano.Aviso(new[] { Linea("90077", eventoId: 7) }, VIVOS), "La plantilla ya inscribe");
            Assert.IsNull(AvisoCursoVendidoAMano.Aviso(new[] { Linea("41000") }, VIVOS));
            Assert.IsNull(AvisoCursoVendidoAMano.Aviso(new[] { Linea("90077") }, new List<AvisoCursoVendidoAMano.EventoConCurso>()), "Sin evento vivo");
            Assert.IsNull(AvisoCursoVendidoAMano.Aviso(new[] { new LineaPedidoVentaDTO { Producto = "90077", tipoLinea = 0, Cantidad = 1 } }, VIVOS), "Texto");
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
