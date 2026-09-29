using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// Nesto#261: consulta de auditoría de enlaces de pago para Administración (solo lectura).
    /// </summary>
    [TestClass]
    public class AuditoriaPagosTPVTests
    {
        private static PagoTPV Pago(int id, string numeroOrden, DateTime fecha, string cliente = "15191",
            string usuario = "NUEVAVISION\\Sancho", string estado = "Pendiente")
        {
            return new PagoTPV
            {
                Id = id,
                NumeroOrden = numeroOrden,
                Empresa = "1  ",
                Cliente = cliente,
                Contacto = "0",
                Importe = 100m,
                FechaCreacion = fecha,
                Usuario = usuario,
                Estado = estado,
                Tipo = "TPVVirtual",
                PagosTPV_Efectos = new List<PagoTPV_Efecto>()
            };
        }

        private static List<PagoTPV> Pagos()
        {
            return new List<PagoTPV>
            {
                Pago(1, "AAAAAAC15191", new DateTime(2026, 9, 1, 10, 0, 0)),
                Pago(2, "BBBBBBC15191", new DateTime(2026, 9, 15, 23, 59, 0), estado: "Autorizado"),
                Pago(3, "CCCCCCC40660", new DateTime(2026, 9, 29, 14, 36, 0), cliente: "40660", usuario: "Inaki"),
                Pago(4, "B9BC22C32366", new DateTime(2025, 1, 15, 10, 30, 0), cliente: "32366", usuario: "NUEVAVISION\\JuanPerez"),
            };
        }

        private static List<int> Filtrar(FiltroAuditoriaPagosTPV filtro)
        {
            return ServicioPagos.AplicarFiltroAuditoria(Pagos().AsQueryable(), filtro).Select(p => p.Id).ToList();
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_SinFiltros_DevuelveTodosDelMasRecienteAlMasAntiguo()
        {
            CollectionAssert.AreEqual(new List<int> { 3, 2, 1, 4 }, Filtrar(new FiltroAuditoriaPagosTPV()));
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_PorFechas_IncluyeElDiaHastaCompleto()
        {
            var ids = Filtrar(new FiltroAuditoriaPagosTPV
            {
                FechaDesde = new DateTime(2026, 9, 1, 18, 0, 0), // la hora no cuenta: desde el principio del día
                FechaHasta = new DateTime(2026, 9, 15)
            });

            CollectionAssert.AreEqual(new List<int> { 2, 1 }, ids);
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_ConNumeroOrden_IgnoraLasFechas()
        {
            var ids = Filtrar(new FiltroAuditoriaPagosTPV
            {
                NumeroOrden = " b9bc22c32366 ",
                FechaDesde = new DateTime(2026, 9, 1),
                FechaHasta = new DateTime(2026, 9, 29)
            });

            CollectionAssert.AreEqual(new List<int> { 4 }, ids);
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_PorUsuario_EncuentraConYSinDominio()
        {
            CollectionAssert.AreEqual(new List<int> { 2, 1 }, Filtrar(new FiltroAuditoriaPagosTPV { Usuario = "Sancho" }));
            CollectionAssert.AreEqual(new List<int> { 3 }, Filtrar(new FiltroAuditoriaPagosTPV { Usuario = "Inaki" }));
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_PorClienteYEstado_SeCombinan()
        {
            CollectionAssert.AreEqual(new List<int> { 2 }, Filtrar(new FiltroAuditoriaPagosTPV { Cliente = " 15191 ", Estado = "Autorizado" }));
        }

        [TestMethod]
        public void AplicarFiltroAuditoria_RespetaElLimiteYNoPasaDelMaximo()
        {
            CollectionAssert.AreEqual(new List<int> { 3, 2 }, Filtrar(new FiltroAuditoriaPagosTPV { Limite = 2 }));

            var muchos = Enumerable.Range(1, FiltroAuditoriaPagosTPV.LIMITE_MAXIMO + 10)
                .Select(i => Pago(i, "X" + i, new DateTime(2026, 1, 1).AddMinutes(i)))
                .AsQueryable();
            Assert.AreEqual(FiltroAuditoriaPagosTPV.LIMITE_MAXIMO,
                ServicioPagos.AplicarFiltroAuditoria(muchos, new FiltroAuditoriaPagosTPV { Limite = 999999 }).Count());
            Assert.AreEqual(FiltroAuditoriaPagosTPV.LIMITE_POR_DEFECTO,
                ServicioPagos.AplicarFiltroAuditoria(muchos, new FiltroAuditoriaPagosTPV()).Count());
        }

        [TestMethod]
        public void MapearAuditoria_RecortaYJuntaLosDocumentosDeLosEfectos()
        {
            PagoTPV pago = Pago(7, "B9BC22C32366", new DateTime(2025, 1, 15, 10, 30, 0), cliente: "32366   ");
            pago.Correo = "cliente@email.com ";
            pago.Movil = "  ";
            pago.PagosTPV_Efectos = new List<PagoTPV_Efecto>
            {
                new PagoTPV_Efecto { Documento = "NV2600001 ", Importe = 60m },
                new PagoTPV_Efecto { Documento = "NV2600002", Importe = 40m },
                new PagoTPV_Efecto { Documento = "NV2600001", Importe = 0m }
            };

            PagoTPVAuditoriaDTO dto = ServicioPagos.MapearAuditoria(pago, "PELUQUERÍA PEPA   ");

            Assert.AreEqual("1", dto.Empresa);
            Assert.AreEqual("32366", dto.Cliente);
            Assert.AreEqual("PELUQUERÍA PEPA", dto.NombreCliente);
            Assert.AreEqual("NUEVAVISION\\Sancho", dto.Usuario);
            Assert.AreEqual("cliente@email.com", dto.Correo);
            Assert.IsNull(dto.Movil, "Sin móvil no se envió SMS");
            Assert.AreEqual(3, dto.NumeroEfectos);
            Assert.AreEqual("NV2600001, NV2600002", dto.Documentos);
        }

        [TestMethod]
        public void MapearAuditoria_SinEfectos_UsaElDocumentoLegacy()
        {
            PagoTPV pago = Pago(8, "X", DateTime.Today);
            pago.PagosTPV_Efectos = null;
            pago.Documento = "NV2600003  ";

            PagoTPVAuditoriaDTO dto = ServicioPagos.MapearAuditoria(pago, null);

            Assert.AreEqual(0, dto.NumeroEfectos);
            Assert.AreEqual("NV2600003", dto.Documentos);
            Assert.IsNull(dto.NombreCliente);
        }

        private static PagosController Controlador(IServicioPagos servicio, params string[] grupos)
        {
            return new PagosController(servicio)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Alguien"), grupos.Select(g => "NUEVAVISION\\" + g).ToArray())
            };
        }

        [TestMethod]
        public async Task Controlador_BuscarAuditoria_SinSerAdministracionNiDireccion_Devuelve403()
        {
            var servicio = A.Fake<IServicioPagos>();
            var controller = Controlador(servicio, NovedadesController.GRUPO_INFORMATICA, "Almacén");

            var resultado = await controller.BuscarAuditoria() as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicio.BuscarAuditoria(A<FiltroAuditoriaPagosTPV>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_BuscarAuditoria_Administracion_PasaLosFiltrosAlServicio()
        {
            var servicio = A.Fake<IServicioPagos>();
            FiltroAuditoriaPagosTPV recibido = null;
            A.CallTo(() => servicio.BuscarAuditoria(A<FiltroAuditoriaPagosTPV>._))
                .Invokes((FiltroAuditoriaPagosTPV f) => recibido = f)
                .Returns(new List<PagoTPVAuditoriaDTO> { new PagoTPVAuditoriaDTO { Id = 1 } });
            var controller = Controlador(servicio, GruposSeguridad.ADMINISTRACION);

            var resultado = await controller.BuscarAuditoria(new DateTime(2026, 9, 1), new DateTime(2026, 9, 29),
                "15191", "Sancho", "Pendiente", "B9BC22C32366", 50) as OkNegotiatedContentResult<List<PagoTPVAuditoriaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual(new DateTime(2026, 9, 1), recibido.FechaDesde);
            Assert.AreEqual(new DateTime(2026, 9, 29), recibido.FechaHasta);
            Assert.AreEqual("15191", recibido.Cliente);
            Assert.AreEqual("Sancho", recibido.Usuario);
            Assert.AreEqual("Pendiente", recibido.Estado);
            Assert.AreEqual("B9BC22C32366", recibido.NumeroOrden);
            Assert.AreEqual(50, recibido.Limite);
        }

        [TestMethod]
        public async Task Controlador_BuscarAuditoria_Direccion_PuedeConsultar()
        {
            var servicio = A.Fake<IServicioPagos>();
            A.CallTo(() => servicio.BuscarAuditoria(A<FiltroAuditoriaPagosTPV>._)).Returns(new List<PagoTPVAuditoriaDTO>());
            var controller = Controlador(servicio, GruposSeguridad.DIRECCION);

            var resultado = await controller.BuscarAuditoria();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<PagoTPVAuditoriaDTO>>));
        }

        [TestMethod]
        public async Task Controlador_BuscarAuditoria_FechasAlReves_Devuelve400()
        {
            var servicio = A.Fake<IServicioPagos>();
            var controller = Controlador(servicio, GruposSeguridad.ADMINISTRACION);

            var resultado = await controller.BuscarAuditoria(new DateTime(2026, 9, 29), new DateTime(2026, 9, 1));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.BuscarAuditoria(A<FiltroAuditoriaPagosTPV>._)).MustNotHaveHappened();
        }
    }
}
