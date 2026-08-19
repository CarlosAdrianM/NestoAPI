using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340 (Agencias, slice A1): los listados de la ventana de Agencias de Nesto servidos
    /// por la API, para que AgenciaService/AgenciasViewModel dejen el EF directo. Cada endpoint
    /// replica el filtro EXACTO del método EF del cliente (pendientes, en curso, tramitados con
    /// sus tres búsquedas, incidentados, reembolsos y retornos).
    /// </summary>
    [TestClass]
    public class EnviosAgenciasListadosTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o =>
                o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            controller = new EnviosAgenciasController(db);
        }

        private void ConEnvios(params EnviosAgencia[] envios)
        {
            var data = envios.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<EnviosAgencia>)fakeEnvios).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<EnviosAgencia>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Provider)
                .Returns(new TestDbAsyncQueryProvider<EnviosAgencia>(data.Provider));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private static EnviosAgencia Envio(int numero, short estado, string empresa = "1", int agencia = 1,
            string cliente = "15191", DateTime? fecha = null, decimal reembolso = 0,
            DateTime? fechaPagoReembolso = null, short retorno = 0, DateTime? fechaRetornoRecibido = null,
            string nombre = "CLIENTE PRUEBA", string direccion = "CALLE MAYOR 1", string telefono = "916281914",
            string movil = "600000000")
        {
            return new EnviosAgencia
            {
                Numero = numero,
                Empresa = empresa,
                Agencia = agencia,
                Cliente = cliente,
                Contacto = "0",
                Estado = estado,
                Fecha = fecha ?? new DateTime(2026, 8, 17),
                Reembolso = reembolso,
                FechaPagoReembolso = fechaPagoReembolso,
                Retorno = retorno,
                FechaRetornoRecibido = fechaRetornoRecibido,
                Nombre = nombre,
                Direccion = direccion,
                Telefono = telefono,
                Movil = movil,
                AgenciasTransporte = new AgenciaTransporte { Nombre = "ASM " }
            };
        }

        [TestMethod]
        public async Task Pendientes_DevuelveSoloEstadosNegativosConSusCampos()
        {
            ConEnvios(
                Envio(1, estado: -1, nombre: "PENDIENTE UNO"),
                Envio(2, estado: 0),
                Envio(3, estado: 1));

            var resultado = await controller.GetEnviosPendientes() as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual(1, resultado.Content.Single().Numero);
            Assert.AreEqual("PENDIENTE UNO", resultado.Content.Single().Nombre);
            Assert.AreEqual("ASM", resultado.Content.Single().NombreAgencia, "El nombre de la agencia viaja aplanado y sin relleno");
        }

        [TestMethod]
        public async Task Listados_ConservanPaddingYLlevanTodasLasColumnas()
        {
            // Nesto#448 (caso real, pedido 924495): el cliente compara Empresa/Cliente/Contacto
            // EN MEMORIA contra otras entidades char ('1  ' != '1' en .NET aunque en SQL casen) y
            // reconstruye EnviosAgencia entero desde el DTO: un campo ausente se machaca a NULL
            // en la BD al modificar (Vendedor, plaza...).
            var envio = Envio(1, estado: -1, empresa: "1  ", cliente: "17649     ");
            envio.Contacto = "0  ";
            envio.Vendedor = "MPP";
            envio.NombrePlaza = "MADRID";
            envio.Nemonico = "MAD";
            envio.TelefonoPlaza = "910000000";
            envio.EmailPlaza = "plaza@asm.es";
            envio.Usuario = @"NUEVAVISION\Aida";
            envio.FechaFactura = new DateTime(2026, 8, 18);
            ConEnvios(envio);

            var resultado = await controller.GetEnviosPendientes() as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            var dto = resultado.Content.Single();
            Assert.AreEqual("1  ", dto.Empresa, "Empresa debe conservar el relleno del char");
            Assert.AreEqual("17649     ", dto.Cliente, "Cliente debe conservar el relleno del char");
            Assert.AreEqual("0  ", dto.Contacto, "Contacto debe conservar el relleno del char");
            Assert.AreEqual("MPP", dto.Vendedor);
            Assert.AreEqual("MADRID", dto.NombrePlaza);
            Assert.AreEqual("MAD", dto.Nemonico);
            Assert.AreEqual("910000000", dto.TelefonoPlaza);
            Assert.AreEqual("plaza@asm.es", dto.EmailPlaza);
            Assert.AreEqual(@"NUEVAVISION\Aida", dto.Usuario);
            Assert.AreEqual(new DateTime(2026, 8, 18), dto.FechaFactura);
        }

        [TestMethod]
        public async Task EnCurso_FiltraPorAgenciaYEstadoInicialOrdenadoPorNumero()
        {
            ConEnvios(
                Envio(22, estado: 0, agencia: 1),
                Envio(11, estado: 0, agencia: 1),
                Envio(33, estado: 0, agencia: 8),
                Envio(44, estado: 1, agencia: 1));

            var resultado = await controller.GetEnviosEnCurso(1) as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new List<int> { 11, 22 }, resultado.Content.Select(e => e.Numero).ToList());
        }

        [TestMethod]
        public async Task Tramitados_SinNingunFiltro_DevuelveBadRequest()
        {
            // El histórico de tramitados es enorme: sin fecha, cliente ni texto no hay listado.
            ConEnvios(Envio(1, estado: 1));

            var resultado = await controller.GetEnviosTramitados("1");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Tramitados_PorFechaYAgencia_IncluyeEntregadosEIncidentadosYOrdenaPorFechaDescendente()
        {
            // #387: ">= TRAMITADO" para que Entregado (2) e Incidentado (3) sigan visibles en la pestaña
            var fecha = new DateTime(2026, 8, 14);
            ConEnvios(
                Envio(1, estado: 1, fecha: fecha),
                Envio(2, estado: 2, fecha: fecha),
                Envio(3, estado: 3, fecha: fecha),
                Envio(4, estado: 0, fecha: fecha),
                Envio(5, estado: 1, fecha: fecha.AddDays(-1)),
                Envio(6, estado: 1, fecha: fecha, agencia: 8));

            var resultado = await controller.GetEnviosTramitados("1", agencia: 1, fecha: fecha) as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEquivalent(new List<int> { 1, 2, 3 }, resultado.Content.Select(e => e.Numero).ToList());
        }

        [TestMethod]
        public async Task Tramitados_PorCliente_Filtra()
        {
            ConEnvios(
                Envio(1, estado: 1, cliente: "15191"),
                Envio(2, estado: 1, cliente: "99999"));

            var resultado = await controller.GetEnviosTramitados("1", cliente: "15191") as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Single().Numero);
        }

        [TestMethod]
        public async Task Tramitados_PorTexto_BuscaEnNombreDireccionYTelefonos()
        {
            ConEnvios(
                Envio(1, estado: 1, nombre: "PELUQUERIA EVA"),
                Envio(2, estado: 1, direccion: "AVENIDA EVA PERON 3"),
                Envio(3, estado: 1, telefono: "911222333"),
                Envio(4, estado: 1, movil: "611222333"),
                Envio(5, estado: 1));

            var porNombre = await controller.GetEnviosTramitados("1", texto: "EVA") as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;
            var porTelefono = await controller.GetEnviosTramitados("1", texto: "11222333") as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            CollectionAssert.AreEquivalent(new List<int> { 1, 2 }, porNombre.Content.Select(e => e.Numero).ToList());
            CollectionAssert.AreEquivalent(new List<int> { 3, 4 }, porTelefono.Content.Select(e => e.Numero).ToList());
        }

        [TestMethod]
        public async Task Incidentados_SoloElEstadoIncidentado()
        {
            // #387: estado de paso, sin filtro de fecha; entregados y devueltos no aparecen
            ConEnvios(
                Envio(1, estado: 3),
                Envio(2, estado: 2),
                Envio(3, estado: 4));

            var resultado = await controller.GetEnviosIncidentados("1") as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Single().Numero);
        }

        [TestMethod]
        public async Task Reembolsos_SoloTramitadosConReembolsoSinPagar()
        {
            ConEnvios(
                Envio(1, estado: 1, reembolso: 50),
                Envio(2, estado: 1, reembolso: 50, fechaPagoReembolso: new DateTime(2026, 8, 1)),
                Envio(3, estado: 1, reembolso: 0),
                Envio(4, estado: 0, reembolso: 50),
                Envio(5, estado: 1, reembolso: 50, agencia: 8));

            var resultado = await controller.GetEnviosReembolsos("1", 1) as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Single().Numero);
        }

        [TestMethod]
        public async Task Retornos_ExcluyeElTipoIndicadoYLosYaRecibidos()
        {
            ConEnvios(
                Envio(1, estado: 1, retorno: 1),
                Envio(2, estado: 1, retorno: 0),                                          // tipo excluido (sin retorno)
                Envio(3, estado: 1, retorno: 1, fechaRetornoRecibido: new DateTime(2026, 8, 1)),
                Envio(4, estado: 1, retorno: 2, fecha: new DateTime(2026, 8, 10)));

            var resultado = await controller.GetEnviosRetornos("1", 1, tipoRetornoExcluido: 0) as OkNegotiatedContentResult<List<EnvioAgenciaListadoDTO>>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new List<int> { 4, 1 }, resultado.Content.Select(e => e.Numero).ToList(),
                "Ordenados por fecha ascendente, como la pestaña de retornos");
        }
    }
}
