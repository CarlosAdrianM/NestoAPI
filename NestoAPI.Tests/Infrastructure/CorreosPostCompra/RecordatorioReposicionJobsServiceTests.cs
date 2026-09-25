using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532 (corte 1): el job nace APAGADO y, en modo sombra, solo escribe al equipo interno.
    /// </summary>
    [TestClass]
    public class RecordatorioReposicionJobsServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);
        private const string EQUIPO = "laura@nuevavision.es,manuelrodriguez@nuevavision.es";

        private ILectorParametrosUsuario lector;
        private IServicioCorreoElectronico correo;
        private List<MailMessage> enviados;
        private int vecesCalculado;
        private string consumiblesPedidos;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            correo = A.Fake<IServicioCorreoElectronico>();
            enviados = new List<MailMessage>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviados.Add(m))
                .Returns(true);
            vecesCalculado = 0;
            consumiblesPedidos = null;
        }

        private void Parametro(string clave, string valor)
            => A.CallTo(() => lector.LeerParametro("1", "(defecto)", clave)).Returns(valor);

        private static ResultadoRecordatorioReposicionDTO Resultado()
        {
            var resultado = new ResultadoRecordatorioReposicionDTO
            {
                Fecha = HOY,
                HistorialDesde = HOY.AddMonths(-24),
                Consumibles = ConsumiblesReposicion.POR_DEFECTO
            };
            resultado.SeAvisarian.Add(new RecordatorioReposicionClienteDTO
            {
                Cliente = "15191",
                Nombre = "PELUQUERÍA ANA",
                Email = "ana@cliente.es",
                VendedorNombre = "Lidia Martin",
                Productos = new List<CandidatoReposicionDTO>
                {
                    new CandidatoReposicionDTO
                    {
                        Cliente = "15191", Producto = "CREMA500", NombreProducto = "Crema <hidratante>", NumeroCompras = 4,
                        IntervaloDias = 30, DiasDesdeUltimaCompra = 40, UltimaCompra = HOY.AddDays(-40), ImporteHabitual = 50m
                    }
                }
            });
            resultado.Descartes.Add(new CandidatoReposicionDTO
            {
                Cliente = "2002", Producto = "TINTE", Motivo = CalculadoraReposicion.MOTIVO_SUSTITUTO + "TINTE2"
            });
            return resultado;
        }

        private Task<ModoRecordatorioReposicion> Procesar(Func<RecordatorioReposicionClienteDTO, Task> rellenar = null)
            => RecordatorioReposicionJobsService.Procesar(lector, correo,
                (consumibles, hoy) =>
                {
                    vecesCalculado++;
                    consumiblesPedidos = consumibles;
                    return Task.FromResult(Resultado());
                },
                rellenar ?? (c => Task.CompletedTask), EQUIPO, HOY);

        [TestMethod]
        public async Task Procesar_SinParametro_NoHaceNada()
        {
            Parametro(NestoAPI.Models.Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION, null);

            ModoRecordatorioReposicion modo = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, modo);
            Assert.AreEqual(0, vecesCalculado, "Apagado no debe ni consultar LinPedidoVta");
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_ConCeroOValorDesconocido_NoHaceNada()
        {
            foreach (string valor in new[] { "0", "1", "Encendido", "" })
            {
                Parametro(NestoAPI.Models.Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION, valor);
                Assert.AreEqual(ModoRecordatorioReposicion.Apagado, await Procesar(), valor);
            }
            Assert.AreEqual(0, vecesCalculado);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_SiFallaElParametro_NoHaceNada()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("BD caída"));

            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, await Procesar());
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_EnSombra_SoloEscribeAlEquipoInternoNuncaAlCliente()
        {
            Parametro(NestoAPI.Models.Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION, " sombra ");
            Parametro(NestoAPI.Models.Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_CONSUMIBLES, "PEL");

            ModoRecordatorioReposicion modo = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Sombra, modo);
            Assert.AreEqual("PEL", consumiblesPedidos);
            MailMessage mail = enviados.Single();
            CollectionAssert.AreEquivalent(new[] { "laura@nuevavision.es", "manuelrodriguez@nuevavision.es" },
                mail.To.Select(t => t.Address).ToList());
            Assert.AreEqual(0, mail.CC.Count);
            Assert.AreEqual(0, mail.Bcc.Count);
            Assert.IsFalse(mail.To.Any(t => t.Address == "ana@cliente.es"));
            StringAssert.StartsWith(mail.Subject, "[Sombra]");
            StringAssert.Contains(mail.Body, "no se ha escrito a ningún cliente");
            StringAssert.Contains(mail.Body, "Crema &lt;hidratante&gt;", "Los nombres van escapados");
            StringAssert.Contains(mail.Body, "Lidia Martin, tu comercial");
            StringAssert.Contains(mail.Body, "misma marca y subgrupo");
        }

        [TestMethod]
        public async Task Procesar_SiFallanLosEnlacesDeLaTienda_ElCorreoSombraSaleIgual()
        {
            Parametro(NestoAPI.Models.Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION, "Sombra");

            _ = await Procesar(c => throw new Exception("PrestaShop no responde"));

            Assert.AreEqual(1, enviados.Count);
        }

        [TestMethod]
        public void ConstruirCorreoSombra_SinDestinatarios_VaAInformatica()
        {
            using (MailMessage mail = RecordatorioReposicionJobsService.ConstruirCorreoSombra(Resultado(), " ; "))
            {
                Assert.AreEqual(NestoAPI.Models.Constantes.Correos.INFORMATICA, mail.To.Single().Address);
            }
        }

        [TestMethod]
        public void MotivoResumido_QuitaElProductoDelFinal()
        {
            Assert.AreEqual("Después ha comprado un producto equivalente (misma marca y subgrupo)",
                RecordatorioReposicionJobsService.MotivoResumido(CalculadoraReposicion.MOTIVO_SUSTITUTO + "X"));
            Assert.AreEqual("Producto sin stock disponible",
                RecordatorioReposicionJobsService.MotivoResumido(CalculadoraReposicion.MOTIVO_SIN_STOCK));
        }

        [TestMethod]
        public void Plantilla_TonoDeServicioConEnlaceYVendedor()
        {
            RecordatorioReposicionClienteDTO correoCliente = Resultado().SeAvisarian.Single();
            correoCliente.Productos[0].EnlaceTienda = "https://tienda.es/crema?utm_source=otra";

            string html = PlantillaRecordatorioReposicion.CuerpoHtml(correoCliente);

            StringAssert.Contains(html, "por si te viene bien tenerlo a mano");
            StringAssert.Contains(html, "https://tienda.es/crema?" + PlantillaRecordatorioReposicion.UTM.Replace("&", "&amp;"));
            Assert.IsFalse(html.Contains("utm_source=otra"));
            StringAssert.Contains(html, "pídeselo a Lidia Martin");
            StringAssert.Contains(html, "15 de agosto");
            Assert.AreEqual("Por si te viene bien reponer Crema <hidratante>", PlantillaRecordatorioReposicion.Asunto(correoCliente));
        }

        [TestMethod]
        public void Plantilla_SinVendedor_NoMencionaAlComercial()
        {
            RecordatorioReposicionClienteDTO correoCliente = Resultado().SeAvisarian.Single();
            correoCliente.VendedorNombre = null;
            Assert.IsFalse(PlantillaRecordatorioReposicion.CuerpoHtml(correoCliente).Contains("comercial"));
        }

        [TestMethod]
        public async Task Controller_GetReposicion_CalculaEnSecoSinMandarNada()
        {
            var selector = A.Fake<ISelectorRecordatoriosReposicion>();
            A.CallTo(() => selector.Calcular("1", new DateTime(2026, 9, 17), A<string>._)).Returns(Resultado());
            var controller = new RecordatorioReposicionController(() => selector, lector, correo);

            var respuesta = await controller.GetReposicion("1", "2026-09-17") as OkNegotiatedContentResult<ResultadoRecordatorioReposicionDTO>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(1, respuesta.Content.Correos);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controller_GetReposicion_FechaMalFormada_BadRequest()
        {
            var controller = new RecordatorioReposicionController(() => A.Fake<ISelectorRecordatoriosReposicion>(), lector, correo);
            Assert.IsInstanceOfType(await controller.GetReposicion("1", "17/09/2026"), typeof(BadRequestErrorMessageResult));
        }
    }
}
