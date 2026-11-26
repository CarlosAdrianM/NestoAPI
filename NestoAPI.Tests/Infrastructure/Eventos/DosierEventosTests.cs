using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
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
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// Sugerencia 1332 (Reina; Carlos 18/11/26): el dosier del evento se sube en la ficha y sale solo, por correo, a todas las
    /// inscritas vivas unas 2 horas antes de empezar, una sola vez cada una.
    /// </summary>
    [TestClass]
    public class DosierEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 18);
        private static readonly byte[] PDF = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\n%%EOF");
        /// <summary>NestoAPI#895: el dosier sale solo dos días antes a las 10:30 (con el recordatorio de las 10:00, en el mismo correo).</summary>
        private static readonly DateTime MOMENTO = HOY.AddDays(-2).AddHours(10).AddMinutes(30);
        private static readonly DateTime ANTES = HOY.AddDays(-2).AddHours(10);

        private static Evento Evento(TimeSpan? inicio = null, TimeSpan? fin = null, bool gratuito = true, bool activo = true) => new Evento
        {
            Id = 9, Empresa = "1", Titulo = "Masterclass Peeling", Fecha = HOY, Activo = activo, Almacen = "REI",
            HoraInicio = inicio, HoraFin = fin, ImporteSenal = gratuito ? 0 : 100, ImporteSenalNoReembolsable = gratuito ? 0 : 60
        };

        private static Evento A_LAS_17() => Evento(new TimeSpan(17, 0, 0), new TimeSpan(19, 0, 0));

        // ---------------- El fichero ----------------

        [TestMethod]
        public void Validar_SoloUnPdfDeVerdadDeHasta10MB()
        {
            ReglasDosierEvento.Validacion ok = ReglasDosierEvento.Validar(new[] { ("C:\\Users\\Reina\\Dosier Peeling.pdf", "application/pdf", PDF) });
            Assert.IsNull(ok.Error);
            Assert.AreEqual("Dosier Peeling.pdf", ok.Dosier.Nombre);

            Assert.IsNull(ReglasDosierEvento.Validar(new[] { ("dosier.PDF", (string)null, PDF) }).Error, "Sin tipo, vale la extensión");
            Assert.IsTrue(ReglasDosierEvento.Validar(new[] { ("dosier.docx", "application/pdf", PDF) }).TipoNoPermitido);
            Assert.IsTrue(ReglasDosierEvento.Validar(new[] { ("dosier.pdf", "image/png", PDF) }).TipoNoPermitido);
            ReglasDosierEvento.Validacion disfrazado = ReglasDosierEvento.Validar(new[] { ("dosier.pdf", "application/pdf", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 }) });
            Assert.IsTrue(disfrazado.TipoNoPermitido);
            StringAssert.Contains(disfrazado.Error, "no es un PDF válido");
            StringAssert.Contains(ReglasDosierEvento.Validar(new[] { ("dosier.pdf", "application/pdf", new byte[0]) }).Error, "está vacío");
            byte[] grande = new byte[ReglasDosierEvento.TAMANO_MAXIMO + 1];
            Array.Copy(PDF, grande, PDF.Length);
            StringAssert.Contains(ReglasDosierEvento.Validar(new[] { ("dosier.pdf", "application/pdf", grande) }).Error, "el máximo es 10 MB");
            StringAssert.Contains(ReglasDosierEvento.Validar(new[] { ("a.pdf", "application/pdf", PDF), ("b.pdf", "application/pdf", PDF) }).Error, "un solo dosier");
            StringAssert.Contains(ReglasDosierEvento.Validar(null).Error, "Envíelo");
        }

        // ---------------- Cuándo ----------------

        [TestMethod]
        public void MomentoEnvio_DosDiasAntesALas1030_SinHora_Nunca()
        {
            Assert.AreEqual(MOMENTO, ReglasDosierEvento.MomentoEnvio(A_LAS_17()));
            Assert.IsNull(ReglasDosierEvento.MomentoEnvio(Evento()), "Carlos 18/11/26: sin hora de inicio no hay envío");
            Assert.AreEqual(MOMENTO, ReglasDosierEvento.MomentoEnvio(Evento(new TimeSpan(1, 0, 0))), "La hora del evento no cuenta: dos días antes");
            Assert.AreEqual(HOY.AddHours(19), ReglasDosierEvento.FinEnvio(A_LAS_17()));
            Assert.AreEqual(HOY.AddDays(1), ReglasDosierEvento.FinEnvio(Evento(new TimeSpan(17, 0, 0))));
        }

        [TestMethod]
        public void TocaEnviar_DesdeDosDiasAntesHastaQueAcaba()
        {
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(A_LAS_17(), MOMENTO.AddMinutes(-1)));
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(A_LAS_17(), MOMENTO));
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(A_LAS_17(), HOY.AddHours(15)), "El mismo día, antes de empezar");
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(A_LAS_17(), HOY.AddHours(18)), "Ya empezado: se manda mientras no acabe");
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(A_LAS_17(), HOY.AddHours(19)), "Acabado: nada");
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(A_LAS_17(), HOY.AddDays(-1).AddHours(16)), "NestoAPI#895: el día antes, sí");
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(A_LAS_17(), HOY.AddDays(-3).AddHours(16)), "Tres días antes, todavía no");
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(Evento(new TimeSpan(17, 0, 0), activo: false), HOY.AddHours(16)));
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(Evento(new TimeSpan(17, 0, 0), gratuito: false), HOY.AddHours(16)), "De pago, igual");
            Assert.IsTrue(ReglasDosierEvento.TocaEnviar(Evento(new TimeSpan(17, 0, 0), gratuito: true), HOY.AddHours(16)), "Gratuito, igual");
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(Evento(), HOY.AddHours(9)), "Sin hora: nunca (ya no hay «desde las 9:00»)");
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(Evento(), HOY.AddHours(20)));
            Assert.IsFalse(ReglasDosierEvento.TocaEnviar(null, HOY.AddHours(16)));
        }

        [TestMethod]
        public void AhoraMadrid_HoraDeInviernoYDeVerano()
        {
            Assert.AreEqual(new DateTime(2026, 11, 18, 16, 0, 0), ReglasDosierEvento.AhoraMadrid(new DateTime(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(new DateTime(2026, 7, 1, 17, 0, 0), ReglasDosierEvento.AhoraMadrid(new DateTime(2026, 7, 1, 15, 0, 0, DateTimeKind.Utc)));
        }

        // ---------------- A quién ----------------

        private static EventoInscripcion Inscripcion(int id, EstadoInscripcionEvento estado, string respuesta = null, string cliente = null) => new EventoInscripcion
        {
            Id = id, EventoId = 9, Empresa = "1", Cliente = cliente ?? ("C" + id), Contacto = "0", Estado = estado.ToString(), AsistenciaRespuesta = respuesta
        };

        [TestMethod]
        public void LoRecibe_LasFormalizadasUnaSolaVez()
        {
            Assert.IsTrue(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Pagada), false));
            Assert.IsTrue(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Facturada), false));
            Assert.IsTrue(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Inscrita), false));
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Reservada), false),
                "Carlos 18/11/26: una reserva sin pagar no está formalizada");
            Assert.IsTrue(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Asistio), false));
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Pagada), true), "Ya lo tiene");
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Anulada), false));
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Caducada), false));
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.Pagada, "NoPodraIr"), false), "«No podré ir»");
            Assert.IsFalse(ReglasDosierEvento.LoRecibe(Inscripcion(1, EstadoInscripcionEvento.NoAsistio), false));
        }

        [TestMethod]
        public void Correo_DeUstedConElPdfAdjunto()
        {
            var dosier = new DosierEvento { EventoId = 9, Nombre = "Dosier Peeling.pdf", Contenido = PDF };
            using (MailMessage antes = ReglasDosierEvento.Correo(A_LAS_17(), dosier, "Luna", "luna@example.com", HOY.AddHours(15)))
            {
                Assert.AreEqual("Dosier de «Masterclass Peeling»", antes.Subject);
                Assert.AreEqual("nuevavision@nuevavision.es", antes.From.Address);
                StringAssert.Contains(antes.Body, "Le enviamos el dosier de");
                StringAssert.Contains(antes.Body, "que empieza hoy a las 17:00");
                Assert.AreEqual("Dosier Peeling.pdf", antes.Attachments.Single().Name);
                Assert.AreEqual("application/pdf", antes.Attachments.Single().ContentType.MediaType);
            }
            using (MailMessage nuevo = ReglasDosierEvento.Correo(A_LAS_17(), dosier, "Luna", "luna@example.com", HOY.AddHours(16), actualizado: true))
            {
                Assert.AreEqual("Dosier actualizado de «Masterclass Peeling»", nuevo.Subject);
                StringAssert.Contains(nuevo.Body, "la versión actualizada del dosier");
                StringAssert.Contains(nuevo.Body, "Sustituye al que le mandamos antes");
            }
            Assert.AreEqual("que ha empezado hoy a las 17:00", ReglasDosierEvento.CuandoEmpieza(A_LAS_17(), HOY.AddHours(17).AddMinutes(30)));
            Assert.AreEqual("que es hoy", ReglasDosierEvento.CuandoEmpieza(Evento(), HOY.AddHours(9)));
        }

        // ---------------- El núcleo del envío ----------------

        private sealed class Escenario
        {
            public readonly IRepositorioDosierEventos Repositorio = A.Fake<IRepositorioDosierEventos>();
            public readonly HashSet<int> Marcadas = new HashSet<int>();
            public readonly List<MailMessage> Correos = new List<MailMessage>();
            public readonly List<EventoInscripcion> Inscripciones = new List<EventoInscripcion>();
            public readonly List<Evento> Eventos = new List<Evento>();
            public DosierEvento Dosier = new DosierEvento { EventoId = 9, Nombre = "Dosier.pdf", Contenido = PDF };
            public Func<MailMessage, bool> Envia = m => true;
            public DateTime Ahora = HOY.AddHours(15);

            public Escenario()
            {
                A.CallTo(() => Repositorio.Disponible()).Returns(true);
                A.CallTo(() => Repositorio.Leer(A<int>._)).ReturnsLazily(() => Task.FromResult(Dosier));
                A.CallTo(() => Repositorio.Enviados(A<IReadOnlyCollection<int>>._)).ReturnsLazily(() => Task.FromResult(new HashSet<int>(Marcadas)));
                A.CallTo(() => Repositorio.Reservar(A<int>._)).ReturnsLazily((int id) => Task.FromResult(Marcadas.Add(id)));
                A.CallTo(() => Repositorio.Soltar(A<int>._)).ReturnsLazily((int id) => { _ = Marcadas.Remove(id); return Task.CompletedTask; });
            }

            public DependenciasDosierEventos Deps()
            {
                IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
                A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
                {
                    bool ok = Envia(m);
                    if (ok)
                    {
                        Correos.Add(m);
                    }
                    return ok;
                });
                return new DependenciasDosierEventos
                {
                    AhoraMadrid = () => Ahora,
                    Repositorio = Repositorio,
                    EventosDelDia = dia => Task.FromResult(Eventos.Where(e => e.Fecha.Date == dia.Date).ToList()),
                    Evento = id => Task.FromResult(Eventos.SingleOrDefault(e => e.Id == id)),
                    Inscripciones = id => Task.FromResult(Inscripciones.Where(i => i.EventoId == id).ToList()),
                    CorreoCliente = (e, c, k) => Task.FromResult(c == "SIN" ? null : c + "@example.com"),
                    NombreCliente = (e, c, k) => Task.FromResult("Cliente " + c),
                    Correo = correo
                };
            }
        }

        [TestMethod]
        public async Task ProcesarHoy_ALaHora_ATodasLasFormalizadasConCorreo_YSoloUnaVez()
        {
            var e = new Escenario();
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            e.Inscripciones.Add(Inscripcion(2, EstadoInscripcionEvento.Reservada));
            e.Inscripciones.Add(Inscripcion(3, EstadoInscripcionEvento.Anulada));
            e.Inscripciones.Add(Inscripcion(4, EstadoInscripcionEvento.Inscrita, "NoPodraIr"));
            e.Inscripciones.Add(Inscripcion(5, EstadoInscripcionEvento.Pagada, cliente: "SIN"));
            e.Inscripciones.Add(Inscripcion(6, EstadoInscripcionEvento.Inscrita));

            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarHoy(e.Deps());

            Assert.AreEqual(2, r.Enviados);
            Assert.AreEqual(1, r.SinCorreo);
            CollectionAssert.AreEquivalent(new[] { "C1@example.com", "C6@example.com" }, e.Correos.Select(c => c.To[0].Address).ToArray(),
                "La reserva sin pagar (2) no lo recibe");
            CollectionAssert.AreEquivalent(new[] { 1, 6 }, e.Marcadas.ToArray());

            ResultadoDosierEventos otraPasada = await DosierEventosJobs.ProcesarHoy(e.Deps());
            Assert.AreEqual(0, otraPasada.Enviados, "La siguiente pasada no repite");
            Assert.AreEqual(2, e.Correos.Count);
        }

        [TestMethod]
        public async Task ProcesarHoy_EventoSinHoraDeInicio_NoSeManda()
        {
            var e = new Escenario { Ahora = HOY.AddHours(12) };
            e.Eventos.Add(Evento());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));

            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(e.Deps())).Enviados);
            Assert.AreEqual(0, e.Correos.Count);
        }

        [TestMethod]
        public async Task ReservaQuePagaALaUltimaHora_LoRecibeAlPagar_ComoUnaInscripcionTardia()
        {
            var e = new Escenario();
            e.Eventos.Add(A_LAS_17());
            EventoInscripcion reserva = Inscripcion(2, EstadoInscripcionEvento.Reservada);
            e.Inscripciones.Add(reserva);

            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(e.Deps())).Enviados, "Sin pagar, no");

            reserva.Estado = EstadoInscripcionEvento.Pagada.ToString();
            ResultadoDosierEventos alPagar = await DosierEventosJobs.AlInscribirse(e.Deps(), A_LAS_17(), reserva);

            Assert.AreEqual(1, alPagar.Enviados);
            Assert.AreEqual("C2@example.com", e.Correos.Single().To[0].Address);
            Assert.AreEqual("Dosier de «Masterclass Peeling»", e.Correos.Single().Subject, "El de siempre, no «actualizado»");
        }

        // ---------------- Sustituirlo pidiendo reenviarlo (Carlos 18/11/26) ----------------

        [TestMethod]
        public void AReenviar_LasFormalizadasQueYaLoTenian_SoloMientrasTocaEnviar()
        {
            var inscripciones = new List<EventoInscripcion>
            {
                Inscripcion(1, EstadoInscripcionEvento.Pagada),
                Inscripcion(2, EstadoInscripcionEvento.Pagada),
                Inscripcion(3, EstadoInscripcionEvento.Anulada),
                Inscripcion(4, EstadoInscripcionEvento.Inscrita, "NoPodraIr"),
                Inscripcion(5, EstadoInscripcionEvento.Reservada)
            };
            var enviados = new HashSet<int> { 1, 3, 4, 5 };

            CollectionAssert.AreEqual(new[] { 1 }, ReglasDosierEvento.AReenviar(A_LAS_17(), inscripciones, enviados, HOY.AddHours(16)),
                "Solo la 1: la 2 no lo tenía (le llega igual), la 3 está anulada, la 4 no irá y la 5 no ha pagado");
            Assert.AreEqual(0, ReglasDosierEvento.AReenviar(A_LAS_17(), inscripciones, enviados, ANTES).Count, "Antes de la hora, nada");
            Assert.AreEqual(0, ReglasDosierEvento.AReenviar(A_LAS_17(), inscripciones, enviados, HOY.AddHours(19)).Count, "Acabado, nada");
            Assert.AreEqual(0, ReglasDosierEvento.AReenviar(Evento(), inscripciones, enviados, HOY.AddHours(16)).Count, "Sin hora, nada");
        }

        [TestMethod]
        public async Task ProcesarEvento_ConReenviar_LasQueLoTeniamLoRecibenComoActualizado_YLasDemasElDeSiempre()
        {
            var e = new Escenario { Ahora = HOY.AddHours(16) };
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            e.Inscripciones.Add(Inscripcion(2, EstadoInscripcionEvento.Pagada));
            e.Inscripciones.Add(Inscripcion(3, EstadoInscripcionEvento.Anulada));
            _ = e.Marcadas.Add(1);
            _ = e.Marcadas.Add(3);

            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarEvento(e.Deps(), 9, reenviar: true);

            Assert.AreEqual(1, r.Reenviados);
            Assert.AreEqual(2, r.Enviados);
            Assert.AreEqual("Dosier actualizado de «Masterclass Peeling»", e.Correos.Single(c => c.To[0].Address == "C1@example.com").Subject);
            Assert.AreEqual("Dosier de «Masterclass Peeling»", e.Correos.Single(c => c.To[0].Address == "C2@example.com").Subject);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, e.Marcadas.ToArray(), "La anulada conserva su marca y no se le reenvía");
        }

        [TestMethod]
        public async Task ProcesarHoy_AntesDeLaHora_SinDosier_OSinScript_Nada()
        {
            var antes = new Escenario { Ahora = ANTES };
            antes.Eventos.Add(A_LAS_17());
            antes.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(antes.Deps())).Enviados);

            var sinDosier = new Escenario { Dosier = null };
            sinDosier.Eventos.Add(A_LAS_17());
            sinDosier.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(sinDosier.Deps())).Enviados, "Sin dosier a la hora, nada");
            Assert.AreEqual(0, sinDosier.Marcadas.Count);

            var sinScript = new Escenario();
            A.CallTo(() => sinScript.Repositorio.Disponible()).Returns(false);
            sinScript.Eventos.Add(A_LAS_17());
            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarHoy(sinScript.Deps());
            Assert.AreEqual(DosierEventosJobs.MOTIVO_SIN_SCRIPT, r.MotivoNoSeEnvia);
        }

        [TestMethod]
        public async Task ElCorreoFalla_SeSueltaYSeReintentaSinRepetirALasDemas()
        {
            var e = new Escenario();
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            e.Inscripciones.Add(Inscripcion(2, EstadoInscripcionEvento.Pagada));
            e.Envia = m => m.To[0].Address != "C2@example.com";

            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarHoy(e.Deps());
            Assert.AreEqual(1, r.Enviados);
            Assert.AreEqual(1, r.Fallidos);
            CollectionAssert.AreEqual(new[] { 1 }, e.Marcadas.ToArray(), "La que falla se suelta");

            e.Envia = m => true;
            ResultadoDosierEventos reintento = await DosierEventosJobs.ProcesarHoy(e.Deps());
            Assert.AreEqual(1, reintento.Enviados);
            Assert.AreEqual("C2@example.com", e.Correos.Last().To[0].Address);
            Assert.AreEqual(2, e.Correos.Count);
        }

        [TestMethod]
        public async Task OtraPasadaSeLaHaLlevado_NoSeManda()
        {
            var e = new Escenario();
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            A.CallTo(() => e.Repositorio.Reservar(1)).Returns(false);

            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(e.Deps())).Enviados);
            Assert.AreEqual(0, e.Correos.Count);
        }

        [TestMethod]
        public async Task AlInscribirse_DespuesDelEnvio_SoloElSuyo_Antes_Nada()
        {
            var e = new Escenario();
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            _ = e.Marcadas.Add(1);
            EventoInscripcion tardia = Inscripcion(2, EstadoInscripcionEvento.Inscrita);
            e.Inscripciones.Add(tardia);

            ResultadoDosierEventos r = await DosierEventosJobs.AlInscribirse(e.Deps(), A_LAS_17(), tardia);
            Assert.AreEqual(1, r.Enviados);
            Assert.AreEqual("C2@example.com", e.Correos.Single().To[0].Address);

            var pronto = new Escenario { Ahora = ANTES };
            Assert.AreEqual(0, (await DosierEventosJobs.AlInscribirse(pronto.Deps(), A_LAS_17(), Inscripcion(3, EstadoInscripcionEvento.Pagada))).Enviados,
                "Antes de la hora le llegará con todas");
        }

        [TestMethod]
        public async Task ProcesarEvento_AlSubirloTarde_SaleAQuienNoLoTenga()
        {
            var e = new Escenario { Ahora = HOY.AddHours(16) };
            e.Eventos.Add(A_LAS_17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            e.Inscripciones.Add(Inscripcion(2, EstadoInscripcionEvento.Pagada));
            _ = e.Marcadas.Add(1);

            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarEvento(e.Deps(), 9);

            Assert.AreEqual(1, r.Enviados, "Sustituirlo no lo reenvía a quien ya tenía el anterior");
            Assert.AreEqual("C2@example.com", e.Correos.Single().To[0].Address);
        }

        // ---------------- NestoAPI#895: dos días antes y en el mismo correo que el enlace ----------------

        private static Evento OnlineALas17() { Evento e = A_LAS_17(); e.Formato = FormatosEvento.ONLINE; return e; }

        [TestMethod]
        public void Correo_DosDiasAntes_ConElDiaYElEnlace()
        {
            var dosier = new DosierEvento { EventoId = 9, Nombre = "Dosier.pdf", Contenido = PDF };
            using (MailMessage mail = ReglasDosierEvento.Correo(OnlineALas17(), dosier, "Luna", "luna@example.com", MOMENTO, false, null, "https://teams.microsoft.com/l/abc"))
            {
                StringAssert.Contains(mail.Body, "que es el mi&#233;rcoles 18 de noviembre a las 17:00, online");
                StringAssert.Contains(mail.Body, "para que pueda imprimirlo con tiempo");
                StringAssert.Contains(mail.Body, "Puede conectarse desde este enlace: <a href='https://teams.microsoft.com/l/abc'>");
                Assert.AreEqual("Dosier.pdf", mail.Attachments.Single().Name);
            }
            using (MailMessage presencial = ReglasDosierEvento.Correo(A_LAS_17(), dosier, "Luna", "luna@example.com", MOMENTO, false, null, "https://teams.microsoft.com/l/abc"))
            {
                Assert.IsFalse(presencial.Body.Contains("Puede conectarse"), "Presencial: solo el dosier");
            }
        }

        [TestMethod]
        public async Task ProcesarHoy_DosDiasAntes_LoMandaConElEnlace()
        {
            var e = new Escenario { Ahora = MOMENTO };
            e.Eventos.Add(OnlineALas17());
            e.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            DependenciasDosierEventos deps = e.Deps();
            deps.EnlaceOnline = id => Task.FromResult("https://teams.microsoft.com/l/abc");

            ResultadoDosierEventos r = await DosierEventosJobs.ProcesarHoy(deps);

            Assert.AreEqual(1, r.Enviados);
            StringAssert.Contains(e.Correos.Single().Body, "https://teams.microsoft.com/l/abc");
            var antes = new Escenario { Ahora = ANTES };
            antes.Eventos.Add(OnlineALas17());
            antes.Inscripciones.Add(Inscripcion(1, EstadoInscripcionEvento.Pagada));
            Assert.AreEqual(0, (await DosierEventosJobs.ProcesarHoy(antes.Deps())).Enviados, "Antes de las 10:30 de dos días antes, el job no (lo lleva el recordatorio)");
        }

        private sealed class EscenarioRecordatorio
        {
            public readonly IRepositorioDosierEventos Dosieres = A.Fake<IRepositorioDosierEventos>();
            public readonly HashSet<int> Marcadas = new HashSet<int>();
            /// <summary>Lo enviado (el correo se desecha al mandarlo: se copia el cuerpo y los adjuntos).</summary>
            public readonly List<(string Body, List<string> Adjuntos)> Correos = new List<(string Body, List<string> Adjuntos)>();
            public bool Envia = true;

            public EscenarioRecordatorio()
            {
                A.CallTo(() => Dosieres.Disponible()).Returns(true);
                A.CallTo(() => Dosieres.Leer(A<int>._)).Returns(Task.FromResult(new DosierEvento { EventoId = 9, Nombre = "Dosier.pdf", Contenido = PDF }));
                A.CallTo(() => Dosieres.Reservar(A<int>._)).ReturnsLazily((int id) => Task.FromResult(Marcadas.Add(id)));
                A.CallTo(() => Dosieres.Soltar(A<int>._)).ReturnsLazily((int id) => { _ = Marcadas.Remove(id); return Task.CompletedTask; });
            }

            public DependenciasRecordatorioEventos Deps()
            {
                IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
                A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
                {
                    if (Envia)
                    {
                        Correos.Add((m.Body, m.Attachments.Select(a => a.Name).ToList()));
                    }
                    return Envia;
                });
                return new DependenciasRecordatorioEventos
                {
                    CorreoCliente = (e, c, k) => Task.FromResult(c + "@example.com"),
                    NombreCliente = (e, c, k) => Task.FromResult("Cliente " + c),
                    Push = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>(),
                    Correo = correo,
                    Dosieres = Dosieres
                };
            }
        }

        private static EventoInscripcion ConClave(EventoInscripcion i) { i.ClaveAsistencia = Guid.NewGuid(); return i; }

        [TestMethod]
        public async Task Recordatorio_LlevaElDosierAdjunto_UnaSolaVez()
        {
            var e = new EscenarioRecordatorio();
            DependenciasRecordatorioEventos deps = e.Deps();
            DosierEvento dosier = await RecordatorioEventosJobsService.DosierDe(deps, OnlineALas17());
            EventoInscripcion pagada = ConClave(Inscripcion(1, EstadoInscripcionEvento.Pagada));

            (bool _, bool correo) = await RecordatorioEventosJobsService.Avisar(deps, OnlineALas17(), pagada, null, "https://teams.microsoft.com/l/abc", dosier);

            Assert.IsTrue(correo);
            (string Body, List<string> Adjuntos) mail = e.Correos.Single();
            Assert.AreEqual("Dosier.pdf", mail.Adjuntos.Single(), "Un solo correo con el enlace y el dosier");
            StringAssert.Contains(mail.Body, "https://teams.microsoft.com/l/abc");
            StringAssert.Contains(mail.Body, "Le adjuntamos el dosier del evento");
            CollectionAssert.AreEqual(new[] { 1 }, e.Marcadas.ToArray(), "Queda marcado: el job del dosier no lo repite");

            _ = await RecordatorioEventosJobsService.Avisar(deps, OnlineALas17(), pagada, null, null, dosier);
            Assert.AreEqual(0, e.Correos.Last().Adjuntos.Count, "Si ya lo tenía, no se repite");
        }

        [TestMethod]
        public async Task Recordatorio_SinPagar_OSinDosier_OElCorreoFalla_NoLoMarca()
        {
            var e = new EscenarioRecordatorio();
            DependenciasRecordatorioEventos deps = e.Deps();
            DosierEvento dosier = await RecordatorioEventosJobsService.DosierDe(deps, A_LAS_17());

            _ = await RecordatorioEventosJobsService.Avisar(deps, A_LAS_17(), ConClave(Inscripcion(2, EstadoInscripcionEvento.Reservada)), null, null, dosier);
            _ = await RecordatorioEventosJobsService.Avisar(deps, A_LAS_17(), ConClave(Inscripcion(3, EstadoInscripcionEvento.Pagada)), null, null, null);
            e.Envia = false;
            _ = await RecordatorioEventosJobsService.Avisar(deps, A_LAS_17(), ConClave(Inscripcion(4, EstadoInscripcionEvento.Pagada)), null, null, dosier);

            Assert.AreEqual(0, e.Marcadas.Count, "Reserva sin pagar, sin dosier o correo fallido: la marca no se queda");
            Assert.IsTrue(e.Correos.All(c => c.Adjuntos.Count == 0));
            A.CallTo(() => e.Dosieres.Soltar(4)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DosierDe_SinScriptOSinRepositorio_Null()
        {
            var e = new EscenarioRecordatorio();
            A.CallTo(() => e.Dosieres.Disponible()).Returns(false);

            Assert.IsNull(await RecordatorioEventosJobsService.DosierDe(e.Deps(), A_LAS_17()));
            Assert.IsNull(await RecordatorioEventosJobsService.DosierDe(new DependenciasRecordatorioEventos(), A_LAS_17()));
        }

        // ---------------- Servicio y endpoints ----------------

        private NVEntities db;
        private List<Evento> eventos;
        private IRepositorioDosierEventos repositorio;
        private List<int> encolados;
        private List<bool> reenvios;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento> { A_LAS_17() };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(new List<EventoInscripcion>()));
            repositorio = A.Fake<IRepositorioDosierEventos>();
            A.CallTo(() => repositorio.Disponible()).Returns(true);
            A.CallTo(() => repositorio.LeerInfo(A<IReadOnlyCollection<int>>._)).ReturnsLazily(() => Task.FromResult(new Dictionary<int, DosierEventoDTO>
            {
                [9] = new DosierEventoDTO { EventoId = 9, Nombre = "Dosier.pdf", Tamano = PDF.Length, Fecha = HOY.AddHours(8), Enviados = 3 }
            }));
            encolados = new List<int>();
            reenvios = new List<bool>();
        }

        private ServicioEventos Servicio(DateTime ahora) => new ServicioEventos(db, () => ahora, (e, u) => u == "Reina" ? "REI" : null,
            A.Fake<IRepositorioFacturacionCursos>(), A.Fake<IRepositorioCupones>(), t => t())
        {
            Dosieres = repositorio,
            AlSubirDosier = (id, reenviar) => { encolados.Add(id); reenvios.Add(reenviar); },
            AhoraMadrid = () => ahora
        };

        [TestMethod]
        public async Task GuardarDosier_YaEsLaHora_LoEncola_Antes_No()
        {
            DosierEventoDTO dto = await Servicio(HOY.AddHours(16)).GuardarDosier(9, new DosierEvento { Nombre = "Dosier.pdf", Contenido = PDF }, "NUEVAVISION\\Reina");
            CollectionAssert.AreEqual(new[] { 9 }, encolados);
            Assert.AreEqual(MOMENTO, dto.EnvioPrevisto);
            Assert.AreEqual(3, dto.Enviados);

            encolados.Clear();
            _ = await Servicio(ANTES).GuardarDosier(9, new DosierEvento { Nombre = "Dosier.pdf", Contenido = PDF }, "NUEVAVISION\\Reina");
            Assert.AreEqual(0, encolados.Count, "Antes de la hora, lo manda el job");
            A.CallTo(() => repositorio.Guardar(9, A<DosierEvento>._, A<string>._)).MustHaveHappenedTwiceExactly();
            CollectionAssert.AreEqual(new[] { false }, reenvios, "Sin pedirlo, no se reenvía");
        }

        [TestMethod]
        public async Task GuardarDosier_ConReenviar_LoPasaAlEnvio()
        {
            _ = await Servicio(HOY.AddHours(16)).GuardarDosier(9, new DosierEvento { Nombre = "Dosier.pdf", Contenido = PDF }, "NUEVAVISION\\Reina", reenviar: true);

            CollectionAssert.AreEqual(new[] { 9 }, encolados);
            CollectionAssert.AreEqual(new[] { true }, reenvios);
        }

        [TestMethod]
        public async Task LeerEventos_SinHoraDeInicio_NoTieneEnvioPrevisto()
        {
            eventos[0].HoraInicio = null;
            eventos[0].HoraFin = null;

            List<EventoDTO> lista = await Servicio(HOY.AddHours(10)).LeerEventos("1", false);

            Assert.IsNull(lista.Single().Dosier.EnvioPrevisto);
        }

        // ---------------- Hora de inicio obligatoria (Carlos 18/11/26) ----------------

        [TestMethod]
        public void HoraInicio_ObligatoriaAlCrear_YAlGuardarUnoDeHoyEnAdelante_NoEnUnoPasado()
        {
            Assert.AreEqual(ReglasHoraInicioEvento.MENSAJE_FALTA, ReglasHoraInicioEvento.MotivoFalta(Evento(), true, HOY));
            Assert.AreEqual(ReglasHoraInicioEvento.MENSAJE_FALTA, ReglasHoraInicioEvento.MotivoFalta(Evento(), false, HOY), "De hoy");
            Assert.IsNull(ReglasHoraInicioEvento.MotivoFalta(Evento(), false, HOY.AddDays(1)), "Pasado: no se toca");
            Assert.AreEqual(ReglasHoraInicioEvento.MENSAJE_FALTA, ReglasHoraInicioEvento.MotivoFalta(Evento(), true, HOY.AddDays(1)), "Al crear, siempre");
            Assert.IsNull(ReglasHoraInicioEvento.MotivoFalta(A_LAS_17(), true, HOY));
        }

        [TestMethod]
        public async Task ModificarEvento_FuturoSinHora_400_YNoSeGuarda()
        {
            eventos[0].HoraInicio = null;
            eventos[0].HoraFin = null;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(HOY.AddHours(10))
                .ModificarEvento(9, new EventoDTO { Titulo = "Masterclass Peeling", Fecha = HOY, Almacen = "REI" }, "NUEVAVISION\\Reina"));

            Assert.AreEqual(ReglasHoraInicioEvento.MENSAJE_FALTA, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerEventos_TraenSuDosier()
        {
            List<EventoDTO> lista = await Servicio(HOY.AddHours(10)).LeerEventos("1", false);

            Assert.AreEqual("Dosier.pdf", lista.Single().Dosier.Nombre);
            Assert.AreEqual(MOMENTO, lista.Single().Dosier.EnvioPrevisto);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private EventosController Controlador(IPrincipal usuario, DateTime ahora) =>
            new EventosController(db, Servicio(ahora)) { Request = new HttpRequestMessage(), User = usuario };

        [TestMethod]
        public async Task Endpoints_SoloQuienMantieneElEvento()
        {
            var vendedor = await Controlador(Usuario("NUEVAVISION\\Jesus", "Ventas"), HOY).DeleteDosier(9) as ResponseMessageResult;
            var otraTienda = await Controlador(Usuario("NUEVAVISION\\Paloma", "Tiendas"), HOY).GetDosier(9) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, vendedor.Response.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, otraTienda.Response.StatusCode);
            Assert.IsInstanceOfType(await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"), HOY).DeleteDosier(99), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Endpoints_SinElScript_503()
        {
            A.CallTo(() => repositorio.Disponible()).Returns(false);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"), HOY).GetDosier(9) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.Response.StatusCode);
        }

        [TestMethod]
        public async Task GetDosier_DevuelveElPdf()
        {
            A.CallTo(() => repositorio.Leer(9)).Returns(Task.FromResult(new DosierEvento { EventoId = 9, Nombre = "Dosier Peeling.pdf", Contenido = PDF }));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"), HOY).GetDosier(9) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.OK, resultado.Response.StatusCode);
            Assert.AreEqual("application/pdf", resultado.Response.Content.Headers.ContentType.MediaType);
            CollectionAssert.AreEqual(PDF, await resultado.Response.Content.ReadAsByteArrayAsync());
        }

        [TestMethod]
        public async Task PostDosier_UnaImagen_415()
        {
            var contenido = new MultipartFormDataContent();
            var fichero = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            fichero.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            contenido.Add(fichero, "fichero", "foto.png");
            EventosController controlador = Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"), HOY);
            controlador.Request = new HttpRequestMessage { Content = contenido };

            var resultado = await controlador.PostDosier(9) as NegotiatedContentResult<System.Web.Http.HttpError>;

            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<int>._, A<DosierEvento>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetEventos_ElClienteDeLaTiendaNoVeElDosier()
        {
            var cliente = await Controlador(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191") }, "prueba")), HOY)
                .GetEventos(soloActivos: false) as OkNegotiatedContentResult<List<EventoDTO>>;
            var vendedor = await Controlador(Usuario("NUEVAVISION\\Jesus", "Ventas"), HOY)
                .GetEventos(soloActivos: false) as OkNegotiatedContentResult<List<EventoDTO>>;

            Assert.IsNull(cliente.Content.Single().Dosier);
            Assert.IsNotNull(vendedor.Content.Single().Dosier);
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
