using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#887 (duda de Paloma, novedad 1421): los datos de la transferencia de una inscripción (IBAN, beneficiario, concepto con el
    /// documento INS{id} y la señal), y que los avisos de plazo de pago por transferencia los lleven en vez de «pídanoslos».
    /// </summary>
    [TestClass]
    public class DatosTransferenciaInscripcionTests
    {
        private static readonly DatosPagoAviso CUENTA = new DatosPagoAviso { Iban = "ES12 3456 7890 1234 5678 9012", Titular = "NUEVA VISION, S.A." };

        private static Evento EventoDePago() => new Evento { Id = 5, Titulo = "Masterclass Rosácea ", ImporteSenal = 100m, ImporteSenalNoReembolsable = 60m, Fecha = new DateTime(2026, 12, 10) };

        private static EventoInscripcion Inscripcion(string estado = "Reservada", string modalidad = null, string formaPago = "Transferencia") => new EventoInscripcion
        {
            Id = 123,
            EventoId = 5,
            Empresa = "1  ",
            Cliente = "29606     ",
            Contacto = "0  ",
            Estado = estado,
            Modalidad = modalidad,
            FormaPago = formaPago
        };

        [TestMethod]
        public void Construir_ConceptoConElClienteYElDocumentoDelCobro_YLaSenalSegunLaModalidad()
        {
            DatosTransferenciaInscripcionDTO d = ReglasDatosTransferenciaInscripcion.Construir(Inscripcion(), EventoDePago(), CUENTA);

            Assert.AreEqual("Cliente 29606 - INS123", d.Concepto);
            Assert.AreEqual(100m, d.Importe);
            Assert.AreEqual("29606", d.Cliente);
            Assert.AreEqual("Masterclass Rosácea", d.Evento);
            Assert.AreEqual("IBAN: ES12 3456 7890 1234 5678 9012" + Environment.NewLine + "Beneficiario: NUEVA VISION, S.A." + Environment.NewLine +
                "Concepto: Cliente 29606 - INS123" + Environment.NewLine + "Importe: 100,00 €", d.Texto);
            StringAssert.Contains(d.Recordatorio, "Cajas › Cobros");
            Assert.AreEqual(60m, ReglasDatosTransferenciaInscripcion.Construir(Inscripcion(modalidad: "NoReembolsable"), EventoDePago(), CUENTA).Importe);
        }

        [TestMethod]
        public void MotivoNoAplica_SoloReservadasDePago()
        {
            Assert.IsNull(ReglasDatosTransferenciaInscripcion.MotivoNoAplica(Inscripcion(), EventoDePago()));
            Assert.IsNotNull(ReglasDatosTransferenciaInscripcion.MotivoNoAplica(Inscripcion("Pagada"), EventoDePago()));
            Assert.IsNotNull(ReglasDatosTransferenciaInscripcion.MotivoNoAplica(Inscripcion("Caducada"), EventoDePago()));
            Assert.IsNotNull(ReglasDatosTransferenciaInscripcion.MotivoNoAplica(Inscripcion(modalidad: "Gratuita"), EventoDePago()));
        }

        [TestMethod]
        public void ComoPagarSinEnlace_ConDatos_LlevaLaCuentaYNoPideQueNosLosPidan()
        {
            DatosTransferenciaInscripcionDTO d = ReglasDatosTransferenciaInscripcion.Construir(Inscripcion(), EventoDePago(), CUENTA);

            string frase = ReglasInscripcionEvento.ComoPagarSinEnlace("Transferencia", d);

            StringAssert.Contains(frase, "ES12 3456 7890 1234 5678 9012");
            StringAssert.Contains(frase, "NUEVA VISION, S.A.");
            StringAssert.Contains(frase, "«Cliente 29606 - INS123»");
            StringAssert.Contains(frase, "100,00 €");
            Assert.IsFalse(frase.Contains("pídanoslos"));
            // Sin IBAN o sin datos, lo de siempre; en efectivo los datos no cambian nada
            StringAssert.Contains(ReglasInscripcionEvento.ComoPagarSinEnlace("Transferencia", new DatosTransferenciaInscripcionDTO()), "pídanoslos");
            StringAssert.Contains(ReglasInscripcionEvento.ComoPagarSinEnlace("Transferencia", null), "pídanoslos");
            Assert.AreEqual("Puede pagarla en su tienda.", ReglasInscripcionEvento.ComoPagarSinEnlace("Efectivo", d));
        }

        [TestMethod]
        public void CorreoPlazoDePago_PorTransferenciaConCuenta_LlevaLosDatos()
        {
            var aviso = new AvisoPagoInscripcion
            {
                Evento = EventoDePago(),
                Inscripcion = Inscripcion(),
                FechaLimite = new DateTime(2026, 12, 3),
                FechaLiberacion = new DateTime(2026, 12, 4)
            };
            aviso.DatosTransferencia = RecordatorioEventosJobsService.DatosTransferencia(e => CUENTA, aviso.Inscripcion, aviso.Evento, "1");

            using (MailMessage correo = RecordatorioEventosJobsService.CorreoPago(aviso, "MARÍA", "maria@example.com"))
            {
                StringAssert.Contains(correo.Body, "ES12 3456 7890 1234 5678 9012");
                StringAssert.Contains(correo.Body, "INS123");
            }
        }

        [TestMethod]
        public void DatosParaElAviso_SoloSiPagaPorTransferencia_YNuncaRompe()
        {
            Assert.IsNull(RecordatorioEventosJobsService.DatosTransferencia(e => CUENTA, Inscripcion(formaPago: "Efectivo"), EventoDePago(), "1"));
            Assert.IsNull(RecordatorioEventosJobsService.DatosTransferencia(null, Inscripcion(), EventoDePago(), "1"));
            Assert.IsNull(RecordatorioEventosJobsService.DatosTransferencia(e => throw new InvalidOperationException("sin BD"), Inscripcion(), EventoDePago(), "1"));
        }

        private static NVEntities Db(List<EventoInscripcion> inscripciones, List<Evento> eventos)
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            return db;
        }

        private static ClaimsPrincipal Empleado() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma") }, "Bearer"));

        private static async Task<HttpStatusCode> Estado(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex.StatusCode;
            }
            return HttpStatusCode.OK;
        }

        [TestMethod]
        public async Task Servicio_ReservaDePago_DevuelveLosDatos_YLosErroresConSuEstado()
        {
            ILectorDatosPagoEmpresa lector = A.Fake<ILectorDatosPagoEmpresa>();
            A.CallTo(() => lector.Leer("1")).Returns(CUENTA);
            var pagada = Inscripcion("Pagada");
            pagada.Id = 124;
            var servicio = new ServicioDatosTransferenciaInscripcion(Db(new List<EventoInscripcion> { Inscripcion(), pagada }, new List<Evento> { EventoDePago() }), lector);

            DatosTransferenciaInscripcionDTO d = await servicio.Leer(123, Empleado());
            Assert.AreEqual("Cliente 29606 - INS123", d.Concepto);

            Assert.AreEqual(HttpStatusCode.Conflict, await Estado(() => servicio.Leer(124, Empleado())));
            Assert.AreEqual(HttpStatusCode.NotFound, await Estado(() => servicio.Leer(999, Empleado())));
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "a@b.es"), new Claim("cliente", "29606") }, "Bearer"));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(() => servicio.Leer(123, cliente)));

            A.CallTo(() => lector.Leer("1")).Returns(new DatosPagoAviso());
            Assert.AreEqual(HttpStatusCode.BadRequest, await Estado(() => servicio.Leer(123, Empleado())));
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
