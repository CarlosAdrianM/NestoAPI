using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#615 (cupones, corte 5): proponer la señal del evento al contabilizar la transferencia. Coincidencia por
    /// inscripción, por concepto, varios candidatos y ninguno; el núcleo común, los enganches (contabilizar y puntear), la
    /// confirmación por el mismo camino que a mano y el correo de la mañana.
    /// </summary>
    [TestClass]
    public class PropuestasSenalesEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 7, 9, 0, 0);
        private static readonly DateTime FECHA_COBRO = new DateTime(2026, 10, 6);

        private static Evento Evento(int id, string titulo, DateTime fecha, decimal senal = 50, bool activo = true) =>
            new Evento { Id = id, Empresa = "1  ", Titulo = titulo, Fecha = fecha, ImporteSenal = senal, Activo = activo };

        private static readonly Evento CLOASMA = Evento(1, "Masterclass Cloasma", new DateTime(2026, 10, 13));
        private static readonly Evento ACTIVOS = Evento(2, "Masterclass Activos", new DateTime(2026, 10, 21));
        private static readonly Evento MICRONEEDLING = Evento(3, "Formación Microneedling (antes ODA ahora Yolanda)", new DateTime(2026, 10, 15), 150);

        private static ApunteParaPropuesta Cobro(decimal importe = 50, params string[] conceptos) => new ApunteParaPropuesta
        {
            Empresa = "1",
            NumOrden = 8001,
            Cliente = "15191     ",
            Contacto = "0  ",
            Fecha = FECHA_COBRO,
            Documento = "A CUENTA",
            ImporteAFavor = importe,
            Conceptos = conceptos.ToList()
        };

        private static EventoInscripcion Inscripcion(int id, int eventoId, EstadoInscripcionEvento estado = EstadoInscripcionEvento.Reservada,
            string cliente = "15191", string contacto = "0") =>
            new EventoInscripcion
            {
                Id = id,
                EventoId = eventoId,
                Empresa = "1",
                Cliente = cliente,
                Contacto = contacto,
                Estado = estado.ToString(),
                FechaCreacion = HOY.AddDays(-3)
            };

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Reglas_PorInscripcion_ProponeElEventoDondeEstaReservada()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(Cobro(50, "S/Pago a cuenta"),
                new[] { CLOASMA, ACTIVOS }, new[] { Inscripcion(7, ACTIVOS.Id) });

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(ACTIVOS.Id, candidatos[0].EventoId);
            Assert.AreEqual(7, candidatos[0].InscripcionId);
            Assert.IsTrue(candidatos[0].PorInscripcion);
            Assert.IsFalse(candidatos[0].PorConcepto);
            Assert.AreEqual("Tiene la inscripción 7 reservada.", candidatos[0].Detalle);
        }

        [TestMethod]
        public void Reglas_PorConcepto_SinInscripcion_ProponeConLaInscripcionQueSeCrearia()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(
                Cobro(50, "S/Pago a cuenta", "TRANSFERENCIA DE CENTRO LUNA SL CONCEPTO SEÑAL MASTERCLASS CLOASMA"),
                new[] { CLOASMA, ACTIVOS }, new EventoInscripcion[0]);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(CLOASMA.Id, candidatos[0].EventoId);
            Assert.IsNull(candidatos[0].InscripcionId);
            Assert.IsTrue(candidatos[0].PorConcepto);
            Assert.AreEqual("El concepto dice «CLOASMA».", candidatos[0].Detalle);
        }

        [TestMethod]
        public void Reglas_PorConcepto_ConLaFechaDelEvento()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(Cobro(50, "RESERVA CURSO 13/10"),
                new[] { CLOASMA, ACTIVOS }, null);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(CLOASMA.Id, candidatos[0].EventoId);
            Assert.AreEqual("El concepto dice «13/10».", candidatos[0].Detalle);
        }

        [TestMethod]
        public void Reglas_InscripcionYConcepto_LoDiceTodo()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(Cobro(50, "masterclass cloasma"),
                new[] { CLOASMA }, new[] { Inscripcion(7, CLOASMA.Id) });

            Assert.AreEqual(1, candidatos.Count);
            Assert.IsTrue(candidatos[0].PorInscripcion && candidatos[0].PorConcepto);
            Assert.AreEqual("Tiene la inscripción 7 reservada y el concepto dice «CLOASMA».", candidatos[0].Detalle);
        }

        [TestMethod]
        public void Reglas_VariosCandidatos_ProponeLaListaConLaInscripcionPrimero()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(Cobro(50, "CURSOS CLOASMA Y ACTIVOS"),
                new[] { CLOASMA, ACTIVOS }, new[] { Inscripcion(9, ACTIVOS.Id) });

            Assert.AreEqual(2, candidatos.Count);
            Assert.AreEqual(ACTIVOS.Id, candidatos[0].EventoId, "Primero el que tiene inscripción");
            Assert.AreEqual(CLOASMA.Id, candidatos[1].EventoId);
        }

        [TestMethod]
        public void Reglas_VariosCandidatosIguales_PorFechaDelEvento()
        {
            List<CandidatoSenal> candidatos = ReglasPropuestaSenal.Candidatos(Cobro(50, "S/Pago a cuenta"),
                new[] { ACTIVOS, CLOASMA }, new[] { Inscripcion(7, ACTIVOS.Id), Inscripcion(8, CLOASMA.Id) });

            CollectionAssert.AreEqual(new[] { CLOASMA.Id, ACTIVOS.Id }, candidatos.Select(c => c.EventoId).ToArray());
        }

        [TestMethod]
        public void Reglas_Ninguno_SoloConElImporteNoSePropone()
        {
            // Carlos (08/10/26): el emparejamiento propone, no adivina. Sin inscripción ni concepto, cualquier transferencia
            // de 50 € saldría como señal de todas las masterclass.
            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(Cobro(50, "S/Pago a cuenta", "TRANSFERENCIA DE CENTRO LUNA SL"),
                new[] { CLOASMA, ACTIVOS }, new EventoInscripcion[0]).Count);
        }

        [TestMethod]
        public void Reglas_Ninguno_SiElImporteNoEsElDeLaSenal()
        {
            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(Cobro(49.99m, "CLOASMA"),
                new[] { CLOASMA }, new[] { Inscripcion(7, CLOASMA.Id) }).Count);
            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(Cobro(100, "CLOASMA"),
                new[] { CLOASMA }, new[] { Inscripcion(7, CLOASMA.Id) }).Count);
        }

        [TestMethod]
        public void Reglas_Ninguno_SiElEventoYaHabiaPasadoOEstaInactivoODeOtraEmpresa()
        {
            Evento pasado = Evento(10, "Masterclass Cloasma", FECHA_COBRO.AddDays(-1));
            Evento inactivo = Evento(11, "Masterclass Cloasma", FECHA_COBRO.AddDays(5), activo: false);
            Evento otraEmpresa = Evento(12, "Masterclass Cloasma", FECHA_COBRO.AddDays(5));
            otraEmpresa.Empresa = "3";

            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(Cobro(50, "CLOASMA"),
                new[] { pasado, inactivo, otraEmpresa }, null).Count);
        }

        [TestMethod]
        public void Reglas_ElDiaDelEventoTodaviaVale()
        {
            Evento hoyMismo = Evento(13, "Masterclass Lentigos", FECHA_COBRO);
            Assert.AreEqual(1, ReglasPropuestaSenal.Candidatos(Cobro(50, "LENTIGOS"), new[] { hoyMismo }, null).Count);
        }

        [TestMethod]
        public void Reglas_LaInscripcionTieneQueSerDelMismoContactoYEstarReservada()
        {
            var inscripciones = new[]
            {
                Inscripcion(7, CLOASMA.Id, contacto: "1"),
                Inscripcion(8, CLOASMA.Id, EstadoInscripcionEvento.Pagada),
                Inscripcion(9, CLOASMA.Id, EstadoInscripcionEvento.Caducada),
                Inscripcion(10, CLOASMA.Id, cliente: "20000")
            };
            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(Cobro(50, "S/Pago a cuenta"), new[] { CLOASMA }, inscripciones).Count);
        }

        [TestMethod]
        public void Reglas_ElCobroDelEnlaceDeInscripcion_NoPasaPorAqui()
        {
            ApunteParaPropuesta cobro = Cobro(50, "CLOASMA");
            cobro.Documento = "INS42";
            Assert.AreEqual(0, ReglasPropuestaSenal.Candidatos(cobro, new[] { CLOASMA }, new[] { Inscripcion(42, CLOASMA.Id) }).Count);
        }

        [TestMethod]
        public void Mencion_PalabraRecortadaPorElBanco()
        {
            Assert.AreEqual("MICRONEED", ReglasPropuestaSenal.MencionDelEvento("TRANSF FORMACION MICRONEED", MICRONEEDLING));
        }

        [TestMethod]
        public void Mencion_SinTildesYEnMinusculas()
        {
            Evento hirsutismo = Evento(4, "Hirsutismo", new DateTime(2026, 10, 19), 150);
            Assert.AreEqual("HIRSUTISMO", ReglasPropuestaSenal.MencionDelEvento("señal hirsutísmo", hirsutismo));
        }

        [TestMethod]
        public void Mencion_LasPalabrasGenericasNoCuentan()
        {
            Assert.IsNull(ReglasPropuestaSenal.MencionDelEvento("PAGO MASTERCLASS", CLOASMA));
            Assert.IsNull(ReglasPropuestaSenal.MencionDelEvento("FORMACION ANTES DE AHORA", MICRONEEDLING));
            CollectionAssert.AreEqual(new[] { "MICRONEEDLING", "YOLANDA" }, ReglasPropuestaSenal.PalabrasDistintivas(MICRONEEDLING.Titulo));
        }

        [TestMethod]
        public void Mencion_FechaConMesEnLetra()
        {
            Assert.AreEqual("13 DE OCTUBRE", ReglasPropuestaSenal.MencionDelEvento("curso del 13 de octubre", CLOASMA));
            Assert.AreEqual("13 OCT", ReglasPropuestaSenal.MencionDelEvento("CURSO 13 OCT", CLOASMA));
            Assert.AreEqual("13-10", ReglasPropuestaSenal.MencionDelEvento("CURSO 13-10-2026", CLOASMA));
        }

        [TestMethod]
        public void Mencion_OtraFechaOUnNumeroLargo_NoCuentan()
        {
            Assert.IsNull(ReglasPropuestaSenal.MencionDelEvento("CURSO 23/10", CLOASMA));
            Assert.IsNull(ReglasPropuestaSenal.MencionDelEvento("FRA 113/101", CLOASMA));
            Assert.IsNull(ReglasPropuestaSenal.MencionDelEvento("REF 2613 OCTANO", CLOASMA));
        }

        // ---------------- Núcleo común (servicio) ----------------

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoSenal> senales;
        private List<ExtractoCliente> extracto;
        private List<EventoInscripcion> inscripciones;
        private List<NestoAPI.Models.Contabilidad> contabilidad;
        private IRepositorioPropuestasSenales repositorio;
        private ILectorConceptosBanco lectorBanco;
        private List<PropuestaSenalEvento> guardadas;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            // En memoria «1  » no es «1» (en SQL sí): los eventos de la BD falsa, sin relleno.
            eventos = new List<Evento> { Evento(1, "Masterclass Cloasma", new DateTime(2026, 10, 13)), Evento(2, "Masterclass Activos", new DateTime(2026, 10, 21)) };
            eventos.ForEach(e => e.Empresa = "1");
            senales = new List<EventoSenal>();
            extracto = new List<ExtractoCliente>();
            inscripciones = new List<EventoInscripcion>();
            contabilidad = new List<NestoAPI.Models.Contabilidad>();
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosSenales).Returns(Crear(senales));
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.Contabilidades).Returns(Crear(contabilidad));
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "CENTRO DE ESTÉTICA LUNA " }
            }));
            repositorio = A.Fake<IRepositorioPropuestasSenales>();
            guardadas = new List<PropuestaSenalEvento>();
            A.CallTo(() => repositorio.Guardar(A<IEnumerable<PropuestaSenalEvento>>._))
                .Invokes((IEnumerable<PropuestaSenalEvento> p) => guardadas.AddRange(p))
                .Returns(Task.CompletedTask);
            lectorBanco = A.Fake<ILectorConceptosBanco>();
            A.CallTo(() => lectorBanco.Leer(A<string>._, A<IEnumerable<int>>._))
                .Returns(Task.FromResult(new Dictionary<int, List<string>>()));
        }

        private ServicioPropuestasSenales Servicio() => new ServicioPropuestasSenales(db, repositorio, lectorBanco, () => HOY, "SenalesEventos");

        private static ExtractoCliente Apunte(int orden, int asiento, decimal pendiente, string documento = "A CUENTA", string tipo = "3",
            string concepto = "S/Pago a cuenta") =>
            new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = orden,
                Asiento = asiento,
                Número = "15191",
                Contacto = "0",
                Fecha = FECHA_COBRO,
                TipoApunte = tipo,
                Nº_Documento = documento,
                Concepto = concepto,
                Importe = pendiente,
                ImportePdte = pendiente
            };

        [TestMethod]
        public async Task Servicio_TrasContabilizar_ProponeConElConceptoDelBanco()
        {
            extracto.Add(Apunte(8001, 555, -50));
            A.CallTo(() => lectorBanco.Leer("1", A<IEnumerable<int>>.That.Contains(555)))
                .Returns(Task.FromResult(new Dictionary<int, List<string>> { [555] = new List<string> { "TRANSFERENCIA CENTRO LUNA", "SEÑAL CLOASMA" } }));

            int propuestas = await Servicio().ProponerParaAsiento("1", 555, OrigenesPropuestaSenal.CONCILIAR);

            Assert.AreEqual(1, propuestas);
            PropuestaSenalEvento p = guardadas.Single();
            Assert.AreEqual(8001, p.NumOrdenExtracto);
            Assert.AreEqual(1, p.EventoId);
            Assert.AreEqual("15191", p.Cliente);
            Assert.AreEqual("0", p.Contacto);
            Assert.AreEqual(50m, p.Importe);
            Assert.IsTrue(p.PorConcepto);
            Assert.AreEqual("TRANSFERENCIA CENTRO LUNA · SEÑAL CLOASMA", p.ConceptoBanco);
            Assert.AreEqual(OrigenesPropuestaSenal.CONCILIAR, p.Origen);
            Assert.AreEqual("SenalesEventos", p.Usuario);
        }

        [TestMethod]
        public async Task Servicio_PorInscripcion_SinConceptoDelBanco()
        {
            extracto.Add(Apunte(8001, 555, -50));
            inscripciones.Add(Inscripcion(7, 2));

            _ = await Servicio().ProponerParaAsiento("1", 555, OrigenesPropuestaSenal.CONTABILIZAR);

            PropuestaSenalEvento p = guardadas.Single();
            Assert.AreEqual(2, p.EventoId);
            Assert.AreEqual(7, p.InscripcionId);
            Assert.IsNull(p.ConceptoBanco);
        }

        [TestMethod]
        public async Task Servicio_UnApunteQueYaEsSenal_NoSePropone()
        {
            extracto.Add(Apunte(8001, 555, -50, concepto: "CLOASMA"));
            senales.Add(new EventoSenal { Id = 1, EventoId = 1, Empresa = "1", NumOrdenExtracto = 8001 });

            Assert.AreEqual(0, await Servicio().ProponerParaAsiento("1", 555, OrigenesPropuestaSenal.CONTABILIZAR));
            A.CallTo(() => repositorio.Guardar(A<IEnumerable<PropuestaSenalEvento>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_SoloCobrosACuentaConAlgoAFavor()
        {
            extracto.Add(Apunte(8001, 555, 0, concepto: "CLOASMA"));                 // ya liquidado
            extracto.Add(Apunte(8002, 555, -50, tipo: "1", concepto: "CLOASMA"));   // no es un pago
            extracto.Add(Apunte(8003, 555, -50, documento: "INS7", concepto: "CLOASMA")); // enlace de inscripción

            Assert.AreEqual(0, await Servicio().ProponerParaAsiento("1", 555, OrigenesPropuestaSenal.CONTABILIZAR));
            A.CallTo(() => lectorBanco.Leer(A<string>._, A<IEnumerable<int>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_TrasPuntear_BuscaElAsientoDeLaLineaDeContabilidad()
        {
            contabilidad.Add(new NestoAPI.Models.Contabilidad { Empresa = "1  ", Nº_Orden = 990, Asiento = 555 });
            extracto.Add(Apunte(8001, 555, -50, concepto: "CLOASMA"));

            Assert.AreEqual(1, await Servicio().ProponerParaApunteContabilidad(990, OrigenesPropuestaSenal.CONCILIAR));
            Assert.AreEqual(0, await Servicio().ProponerParaApunteContabilidad(991, OrigenesPropuestaSenal.CONCILIAR));
        }

        [TestMethod]
        public async Task Servicio_Barrido_SoloImportesDeSenalDeEventosQueNoHanPasado()
        {
            extracto.Add(Apunte(8001, 555, -50, concepto: "CLOASMA"));
            extracto.Add(Apunte(8002, 556, -75, concepto: "CLOASMA"));

            Assert.AreEqual(1, await Servicio().ProponerRecientes("1", HOY.AddDays(-15)));
            Assert.AreEqual(8001, guardadas.Single().NumOrdenExtracto);
            Assert.AreEqual(OrigenesPropuestaSenal.BARRIDO, guardadas.Single().Origen);
        }

        [TestMethod]
        public async Task Servicio_Barrido_SinEventos_NoMiraElExtracto()
        {
            eventos.Clear();

            Assert.AreEqual(0, await Servicio().ProponerRecientes("1", HOY.AddDays(-15)));
            A.CallTo(() => db.ExtractosCliente).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_LeerPendientes_AgrupaPorApunteYEscondeLoQueYaNoVale()
        {
            extracto.Add(Apunte(8001, 555, -50));
            extracto.Add(Apunte(8002, 556, -50));
            extracto.Add(Apunte(8003, 557, 0));    // ya liquidado
            senales.Add(new EventoSenal { Id = 1, EventoId = 1, Empresa = "1", NumOrdenExtracto = 8002 }); // marcada a mano
            A.CallTo(() => repositorio.Pendientes("1")).Returns(Task.FromResult(new List<PropuestaSenalEvento>
            {
                Propuesta(11, 8001, 1, porConcepto: true),
                Propuesta(12, 8001, 2, porInscripcion: true),
                Propuesta(13, 8002, 1),
                Propuesta(14, 8003, 1)
            }));

            List<PropuestaSenalDTO> pendientes = await Servicio().LeerPendientes("1");

            Assert.AreEqual(1, pendientes.Count);
            PropuestaSenalDTO p = pendientes[0];
            Assert.AreEqual(8001, p.NumOrdenExtracto);
            Assert.AreEqual("CENTRO DE ESTÉTICA LUNA", p.Nombre);
            Assert.AreEqual(50m, p.Importe);
            CollectionAssert.AreEqual(new[] { 12, 11 }, p.Candidatos.Select(c => c.Id).ToArray(), "Primero la de la inscripción");
            Assert.AreEqual("Masterclass Activos", p.Candidatos[0].Evento);
        }

        private static PropuestaSenalEvento Propuesta(int id, int orden, int eventoId, bool porInscripcion = false, bool porConcepto = false,
            string estado = EstadosPropuestaSenal.PROPUESTA) =>
            new PropuestaSenalEvento
            {
                Id = id,
                Empresa = "1",
                NumOrdenExtracto = orden,
                EventoId = eventoId,
                Cliente = "15191",
                Contacto = "0",
                Importe = 50,
                PorInscripcion = porInscripcion,
                PorConcepto = porConcepto,
                Detalle = "motivo",
                Estado = estado,
                Origen = OrigenesPropuestaSenal.BARRIDO,
                Usuario = "SenalesEventos",
                FechaCreacion = HOY
            };

        // ---------------- Enganches (contabilizar y puntear) ----------------

        private static PreContabilidad Linea(string tipoCuenta, decimal debe, decimal haber, int? liquidado = null, string empresa = "1  ") =>
            new PreContabilidad { Empresa = empresa, TipoApunte = "3", TipoCuenta = tipoCuenta, Debe = debe, Haber = haber, Liquidado = liquidado };

        [TestMethod]
        public void Encolador_ConElInterruptorApagado_NoHaceNada()
        {
            var encolados = new List<string>();
            var encolador = new EncoladorPropuestasSenales(() => false, (e, a) => encolados.Add(e + "/" + a), n => encolados.Add("p" + n));

            encolador.TrasContabilizar(new[] { Linea("2", 0, 50), Linea("1", 50, 0) }, 555);
            encolador.TrasPuntear(1, 990);

            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void Encolador_CobroACuentaDeCliente_EncolaElAsientoPorEmpresa()
        {
            var encolados = new List<string>();
            var encolador = new EncoladorPropuestasSenales(() => true, (e, a) => encolados.Add(e + "/" + a), n => encolados.Add("p" + n));

            encolador.TrasContabilizar(new[] { Linea("2", 0, 50), Linea("2", 0, 20, empresa: "3"), Linea("1", 70, 0) }, 555);

            CollectionAssert.AreEqual(new[] { "1/555", "3/555" }, encolados);
        }

        [TestMethod]
        public void Encolador_UnCobroQueLiquidaUnaFacturaOUnCargo_NoEncola()
        {
            var encolados = new List<string>();
            var encolador = new EncoladorPropuestasSenales(() => true, (e, a) => encolados.Add(e + "/" + a), n => encolados.Add("p" + n));

            encolador.TrasContabilizar(new[] { Linea("2", 0, 50, liquidado: 777), Linea("2", 50, 0), Linea("3", 0, 50) }, 555);
            encolador.TrasContabilizar(new[] { Linea("2", 0, 50) }, 0);

            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void Encolador_TrasPuntear_SoloConBancoYContabilidad()
        {
            var encolados = new List<string>();
            var encolador = new EncoladorPropuestasSenales(() => true, (e, a) => encolados.Add(e + "/" + a), n => encolados.Add("p" + n));

            encolador.TrasPuntear(null, 990);
            encolador.TrasPuntear(1, null);
            encolador.TrasPuntear(1, 990);

            CollectionAssert.AreEqual(new[] { "p990" }, encolados);
        }

        [TestMethod]
        public void Encolador_SiFallaElEncolado_NoFallaLaContabilizacion()
        {
            var encolador = new EncoladorPropuestasSenales(() => true, (e, a) => throw new InvalidOperationException("Hangfire caído"),
                n => throw new InvalidOperationException("Hangfire caído"));

            encolador.TrasContabilizar(new[] { Linea("2", 0, 50) }, 555);
            encolador.TrasPuntear(1, 990);
        }

        // ---------------- Confirmar y descartar (controlador) ----------------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal ADMINISTRACION = Usuario("NUEVAVISION\\Alfredo", Constantes.GruposSeguridad.ADMINISTRACION);
        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");

        private IServicioEventos eventosServicio;
        private IServicioInscripcionesEventos inscripcionesServicio;
        private IServicioPropuestasSenales propuestasServicio;

        private EventosController Controlador(IPrincipal usuario, bool facturar = true, bool proponer = true)
        {
            eventosServicio = A.Fake<IServicioEventos>();
            A.CallTo(() => eventosServicio.MarcarSenal(A<int>._, A<MarcarSenalEventoDTO>._, A<string>._))
                .ReturnsLazily((int eventoId, MarcarSenalEventoDTO p, string u) => Task.FromResult(new SenalEventoDTO
                {
                    Id = 40,
                    EventoId = eventoId,
                    Empresa = p.Empresa ?? "1",
                    Cliente = p.Cliente,
                    Contacto = p.Contacto,
                    NumOrdenExtracto = p.NumOrdenExtracto
                }));
            inscripcionesServicio = A.Fake<IServicioInscripcionesEventos>();
            propuestasServicio = A.Fake<IServicioPropuestasSenales>();
            return new EventosController(db, eventosServicio, inscripcionesServicio, propuestasServicio)
            {
                Request = new HttpRequestMessage(),
                User = usuario,
                FacturarCursoAlCobrar = () => facturar,
                ProponerSenales = () => proponer,
                Hoy = () => HOY.Date
            };
        }

        [TestMethod]
        public async Task Confirmar_MarcaLaSenalPorElMismoCaminoQueAMano()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12)).Returns(Task.FromResult(Propuesta(12, 8001, 2, porInscripcion: true)));

            var resultado = await controlador.PostConfirmarPropuestaSenal(12) as OkNegotiatedContentResult<SenalEventoDTO>;

            Assert.AreEqual(40, resultado.Content.Id);
            A.CallTo(() => eventosServicio.MarcarSenal(2, A<MarcarSenalEventoDTO>.That.Matches(p =>
                p.Empresa == "1" && p.NumOrdenExtracto == 8001 && p.Cliente == "15191" && p.Contacto == "0"), "NUEVAVISION\\Alfredo"))
                .MustHaveHappenedOnceExactly();
            // Corte 2: paga la inscripción (o la crea) y de ahí la factura del curso y el cupón
            A.CallTo(() => inscripcionesServicio.InscribirDesdeTransferencia(40, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            A.CallTo(() => propuestasServicio.Resolver("1", 8001, 2, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Confirmar_ConLaFacturacionApagada_SoloMarcaLaSenalComoEn591()
        {
            EventosController controlador = Controlador(ADMINISTRACION, facturar: false, proponer: false);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12)).Returns(Task.FromResult(Propuesta(12, 8001, 1)));

            _ = await controlador.PostConfirmarPropuestaSenal(12);

            A.CallTo(() => inscripcionesServicio.InscribirDesdeTransferencia(A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => propuestasServicio.Resolver("1", 8001, 1, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Confirmar_UnaYaDescartada_409()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12))
                .Returns(Task.FromResult(Propuesta(12, 8001, 1, estado: EstadosPropuestaSenal.DESCARTADA)));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => controlador.PostConfirmarPropuestaSenal(12));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Esta propuesta ya está descartada por SenalesEventos.", ex.Message);
            A.CallTo(() => eventosServicio.MarcarSenal(A<int>._, A<MarcarSenalEventoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Confirmar_QueNoExiste_404()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(99)).Returns(Task.FromResult<PropuestaSenalEvento>(null));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => controlador.PostConfirmarPropuestaSenal(99));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task Confirmar_SinPermiso_403()
        {
            EventosController controlador = Controlador(VENDEDOR);

            var resultado = await controlador.PostConfirmarPropuestaSenal(12) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => propuestasServicio.LeerPropuesta(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Confirmar_SiFallaCerrarLasPropuestas_LaSenalQuedaMarcada()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12)).Returns(Task.FromResult(Propuesta(12, 8001, 1)));
            A.CallTo(() => propuestasServicio.Resolver(A<string>._, A<int>._, A<int?>._, A<string>._)).Throws(new InvalidOperationException("boom"));

            var resultado = await controlador.PostConfirmarPropuestaSenal(12) as OkNegotiatedContentResult<SenalEventoDTO>;

            Assert.IsNotNull(resultado);
        }

        [TestMethod]
        public async Task Descartar_204()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12)).Returns(Task.FromResult(Propuesta(12, 8001, 1)));
            A.CallTo(() => propuestasServicio.Descartar(12, "NUEVAVISION\\Alfredo")).Returns(Task.FromResult(true));

            var resultado = await controlador.PostDescartarPropuestaSenal(12) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.NoContent, resultado.StatusCode);
        }

        [TestMethod]
        public async Task Descartar_SiOtroSeAdelanta_409()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            A.CallTo(() => propuestasServicio.LeerPropuesta(12)).Returns(Task.FromResult(Propuesta(12, 8001, 1)));
            A.CallTo(() => propuestasServicio.Descartar(12, A<string>._)).Returns(Task.FromResult(false));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => controlador.PostDescartarPropuestaSenal(12));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
        }

        [TestMethod]
        public async Task PostSenal_AMano_CierraLasPropuestasDelApunteSoloConElInterruptor()
        {
            EventosController encendido = Controlador(ADMINISTRACION, facturar: false, proponer: true);
            _ = await encendido.PostSenal(1, new MarcarSenalEventoDTO { Empresa = "1", NumOrdenExtracto = 8001 });
            A.CallTo(() => propuestasServicio.Resolver("1", 8001, 1, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();

            EventosController apagado = Controlador(ADMINISTRACION, facturar: false, proponer: false);
            _ = await apagado.PostSenal(1, new MarcarSenalEventoDTO { Empresa = "1", NumOrdenExtracto = 8001 });
            A.CallTo(() => propuestasServicio.Resolver(A<string>._, A<int>._, A<int?>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Buscar_MiraLosUltimos15DiasYDevuelveLasPendientes()
        {
            EventosController controlador = Controlador(ADMINISTRACION);
            var pendientes = new List<PropuestaSenalDTO> { new PropuestaSenalDTO { NumOrdenExtracto = 8001 } };
            A.CallTo(() => propuestasServicio.LeerPendientes("1")).Returns(Task.FromResult(pendientes));

            var resultado = await controlador.PostBuscarPropuestasSenales(null) as OkNegotiatedContentResult<List<PropuestaSenalDTO>>;

            A.CallTo(() => propuestasServicio.ProponerRecientes("1", HOY.Date.AddDays(-15))).MustHaveHappenedOnceExactly();
            Assert.AreSame(pendientes, resultado.Content);
        }

        [TestMethod]
        public async Task Buscar_ConDiasFueraDeRango_400()
        {
            EventosController controlador = Controlador(ADMINISTRACION);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                controlador.PostBuscarPropuestasSenales(new BuscarPropuestasSenalesDTO { Dias = 365 }));

            Assert.AreEqual("Indique entre 1 y 90 días.", ex.Message);
        }

        // ---------------- Correo de la mañana ----------------

        private static async Task<(bool enviado, MailMessage correo)> Correo(List<PropuestaSenalDTO> propuestas, Func<Task> buscar = null)
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage capturado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => capturado = new MailMessage { Subject = m.Subject, Body = m.Body, IsBodyHtml = m.IsBodyHtml })
                .Returns(true);
            bool enviado = await SenalesEventosJobsService.Procesar(new DependenciasSenalesEventos
            {
                LeerSenales = () => Task.FromResult(new List<SenalEventoDTO>()),
                Correo = correo,
                Ahora = HOY,
                BuscarPropuestas = buscar,
                LeerPropuestas = propuestas == null ? (Func<Task<List<PropuestaSenalDTO>>>)null : () => Task.FromResult(propuestas)
            });
            return (enviado, capturado);
        }

        private static PropuestaSenalDTO PropuestaCorreo(params CandidatoSenalDTO[] candidatos) => new PropuestaSenalDTO
        {
            Empresa = "1",
            NumOrdenExtracto = 8001,
            Cliente = "15191",
            Contacto = "0",
            Nombre = "CENTRO DE ESTÉTICA LUNA",
            FechaApunte = FECHA_COBRO,
            Concepto = "S/Pago a cuenta",
            ConceptoBanco = "SEÑAL CLOASMA",
            Importe = 50,
            Candidatos = candidatos.ToList()
        };

        private static CandidatoSenalDTO CandidatoCorreo(int id, string evento) =>
            new CandidatoSenalDTO { Id = id, EventoId = id, Evento = evento, FechaEvento = new DateTime(2026, 10, 13), Motivo = "El concepto dice «CLOASMA»." };

        [TestMethod]
        public async Task Correo_ConPropuestasSinConfirmar_SaleAunqueNoHayaNadaMas()
        {
            var (enviado, correo) = await Correo(new List<PropuestaSenalDTO> { PropuestaCorreo(CandidatoCorreo(1, "Masterclass Cloasma")) });

            Assert.IsTrue(enviado);
            StringAssert.Contains(correo.Subject, "1 por confirmar");
            StringAssert.Contains(correo.Body, "Señales propuestas sin confirmar");
            StringAssert.Contains(correo.Body, "<td>SE&#209;AL CLOASMA</td>", "El concepto del banco, que dice más que el del extracto");
            StringAssert.Contains(correo.Body, "Masterclass Cloasma (13/10/2026)");
            StringAssert.Contains(correo.Body, "confirme el evento que corresponda");
        }

        [TestMethod]
        public async Task Correo_ConVariosCandidatos_PideElegir()
        {
            var (_, correo) = await Correo(new List<PropuestaSenalDTO>
            {
                PropuestaCorreo(CandidatoCorreo(1, "Masterclass Cloasma"), CandidatoCorreo(2, "Masterclass Activos"))
            });

            StringAssert.Contains(correo.Body, "Varios, elija uno");
        }

        [TestMethod]
        public async Task Correo_SinPropuestasNiNadaNuevo_NoSale()
        {
            var (enviado, _) = await Correo(new List<PropuestaSenalDTO>());
            Assert.IsFalse(enviado);

            var (enviadoApagado, _) = await Correo(null);
            Assert.IsFalse(enviadoApagado);
        }

        [TestMethod]
        public async Task Correo_ElBarridoVaAntesYSiFallaElCorreoSaleIgual()
        {
            var orden = new List<string>();
            var propuestas = new List<PropuestaSenalDTO> { PropuestaCorreo(CandidatoCorreo(1, "Masterclass Cloasma")) };

            var (enviado, _) = await Correo(propuestas, () => { orden.Add("barrido"); throw new InvalidOperationException("boom"); });

            Assert.IsTrue(enviado);
            CollectionAssert.AreEqual(new[] { "barrido" }, orden);
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return fake;
        }
    }
}
