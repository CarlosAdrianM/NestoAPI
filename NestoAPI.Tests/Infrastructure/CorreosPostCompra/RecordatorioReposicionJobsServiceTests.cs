using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: el job nace APAGADO; en Sombra solo escribe al equipo interno; en Activo escribe a los clientes de los
    /// vendedores de la lista, los registra y manda el resumen.
    /// </summary>
    [TestClass]
    public class RecordatorioReposicionJobsServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);
        private const string EQUIPO = "laura@nuevavision.es,manuelrodriguez@nuevavision.es";
        private const string BUZON = "nuevavision@nuevavision.es";

        private ILectorParametrosUsuario lector;
        private IServicioCorreoElectronico correo;
        private IRepositorioRecordatorioReposicion repositorio;
        private List<MailMessage> enviados;
        private List<(string Cliente, string Grupo, string Version, int? TextoId, int Productos)> registrados;
        private List<EnvioPrevioReposicion> previos;
        private BajasCorreosPostCompra bajas;
        private int vecesCalculado;
        private string consumiblesPedidos;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_VENDEDORES, "NV,(NULL),LM");
            correo = A.Fake<IServicioCorreoElectronico>();
            enviados = new List<MailMessage>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviados.Add(m))
                .Returns(true);

            registrados = new List<(string, string, string, int?, int)>();
            previos = new List<EnvioPrevioReposicion>();
            bajas = new BajasCorreosPostCompra();
            repositorio = A.Fake<IRepositorioRecordatorioReposicion>();
            A.CallTo(() => repositorio.ExistenTablas()).Returns(true);
            A.CallTo(() => repositorio.LeerBajas("1")).ReturnsLazily(() => Task.FromResult(bajas));
            A.CallTo(() => repositorio.LeerEnviosDesde("1", A<DateTime>._)).ReturnsLazily(() => Task.FromResult(previos));
            A.CallTo(() => repositorio.GuardarTexto("1", HOY, A<TextoRecordatorioReposicion>._)).Returns(77);
            A.CallTo(() => repositorio.RegistrarEnvio("1", A<RecordatorioReposicionClienteDTO>._, A<string>._, A<DateTime>._, A<int?>._, A<string>._))
                .Invokes((string e, RecordatorioReposicionClienteDTO c, string grupo, DateTime f, int? id, string version)
                    => registrados.Add((c.Cliente, grupo, version, id, c.Productos.Count)))
                .Returns(Task.CompletedTask);

            vecesCalculado = 0;
            consumiblesPedidos = null;
        }

        private void Parametro(string clave, string valor)
            => A.CallTo(() => lector.LeerParametro("1", "(defecto)", clave)).Returns(valor);

        private void Modo(string valor) => Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION, valor);

        private static CandidatoReposicionDTO Producto(string cliente, string id, string nombre, decimal importe = 50m)
            => new CandidatoReposicionDTO
            {
                Cliente = cliente, Producto = id, NombreProducto = nombre, NumeroCompras = 4, IntervaloDias = 30,
                DiasDesdeUltimaCompra = 40, UltimaCompra = HOY.AddDays(-40), ImporteHabitual = importe, CantidadHabitual = 2
            };

        private static RecordatorioReposicionClienteDTO Cliente(string cliente, string vendedor, string vendedorNombre, bool control = false)
            => new RecordatorioReposicionClienteDTO
            {
                Cliente = cliente,
                Nombre = "PELUQUERÍA " + cliente,
                Email = cliente + "@cliente.es",
                Vendedor = vendedor,
                VendedorNombre = vendedorNombre,
                GrupoControl = control,
                Productos = new List<CandidatoReposicionDTO> { Producto(cliente, "CREMA500", "CREMA <HIDRATANTE> 500 ML") }
            };

        /// <summary>15191 (LM, en la lista), 15200 (XYZ, fuera), 15250 (sin vendedor, en la lista) y 15300 de control (LM).</summary>
        private static ResultadoRecordatorioReposicionDTO Resultado()
        {
            var resultado = new ResultadoRecordatorioReposicionDTO
            {
                Fecha = HOY,
                HistorialDesde = HOY.AddMonths(-24),
                Consumibles = ConsumiblesReposicion.POR_DEFECTO
            };
            resultado.SeAvisarian.Add(Cliente("15191", "LM", "Lidia"));
            resultado.SeAvisarian.Add(Cliente("15200", "XYZ", "Pepe"));
            resultado.SeAvisarian.Add(Cliente("15250", null, null));
            resultado.GrupoControl.Add(Cliente("15300", "LM", "Lidia", control: true));
            resultado.Descartes.Add(new CandidatoReposicionDTO
            {
                Cliente = "2002", Producto = "TINTE", Motivo = CalculadoraReposicion.MOTIVO_SUSTITUTO + "TINTE2"
            });
            return resultado;
        }

        private DependenciasRecordatorioReposicion Dependencias(Func<RecordatorioReposicionClienteDTO, Task> rellenar = null,
            IGeneradorTextoRecordatorioReposicion generador = null, bool conRepositorio = true)
            => new DependenciasRecordatorioReposicion
            {
                Lector = lector,
                Correo = correo,
                Calcular = (consumibles, hoy) =>
                {
                    vecesCalculado++;
                    consumiblesPedidos = consumibles;
                    return Task.FromResult(Resultado());
                },
                RellenarProductos = rellenar ?? (c => Task.CompletedTask),
                Repositorio = conRepositorio ? repositorio : null,
                GeneradorTexto = generador,
                Destinatarios = EQUIPO,
                Remitente = BUZON,
                PausaEntreCorreos = TimeSpan.Zero,
                Hoy = HOY,
                Ahora = () => HOY.AddHours(9)
            };

        private Task<EjecucionRecordatorioReposicionDTO> Procesar(Func<RecordatorioReposicionClienteDTO, Task> rellenar = null,
            IGeneradorTextoRecordatorioReposicion generador = null, bool conRepositorio = true)
            => RecordatorioReposicionJobsService.Procesar(Dependencias(rellenar, generador, conRepositorio));

        private List<MailMessage> CorreosAClientes() => enviados.Where(m => m.To.Any(t => t.Address.EndsWith("@cliente.es"))).ToList();

        private MailMessage Resumen() => enviados.Single(m => m.To.Any(t => t.Address == "laura@nuevavision.es"));

        #region Interruptor

        [TestMethod]
        public async Task Procesar_SinParametro_NoHaceNada()
        {
            Modo(null);

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, ejecucion.Modo);
            Assert.AreEqual(0, vecesCalculado, "Apagado no debe ni consultar LinPedidoVta");
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_ConCeroOValorDesconocido_NoHaceNada()
        {
            foreach (string valor in new[] { "0", "1", "Encendido", "" })
            {
                Modo(valor);
                Assert.AreEqual(ModoRecordatorioReposicion.Apagado, (await Procesar()).Modo, valor);
            }
            Assert.AreEqual(0, vecesCalculado);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_SiFallaElParametro_NoHaceNada()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("BD caída"));

            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, (await Procesar()).Modo);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public void InterpretarModo_ActivoSombraYElRestoApagado()
        {
            Assert.AreEqual(ModoRecordatorioReposicion.Activo, RecordatorioReposicionJobsService.InterpretarModo(" activo "));
            Assert.AreEqual(ModoRecordatorioReposicion.Sombra, RecordatorioReposicionJobsService.InterpretarModo("SOMBRA"));
            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, RecordatorioReposicionJobsService.InterpretarModo("Apagado"));
            Assert.AreEqual(ModoRecordatorioReposicion.Apagado, RecordatorioReposicionJobsService.InterpretarModo(null));
        }

        #endregion

        #region Sombra

        [TestMethod]
        public async Task Procesar_EnSombra_SoloEscribeAlEquipoInternoNuncaAlCliente()
        {
            Modo(" sombra ");
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_CONSUMIBLES, "PEL");

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Sombra, ejecucion.Modo);
            Assert.AreEqual("PEL", consumiblesPedidos);
            MailMessage mail = enviados.Single();
            CollectionAssert.AreEquivalent(new[] { "laura@nuevavision.es", "manuelrodriguez@nuevavision.es" },
                mail.To.Select(t => t.Address).ToList());
            Assert.AreEqual(0, mail.CC.Count);
            Assert.AreEqual(0, mail.Bcc.Count);
            StringAssert.StartsWith(mail.Subject, "[Sombra]");
            string body = WebUtility.HtmlDecode(mail.Body);
            StringAssert.Contains(body, "no se ha escrito a ningún cliente");
            StringAssert.Contains(mail.Body, "CREMA &lt;HIDRATANTE&gt;", "Los nombres van escapados");
            StringAssert.Contains(body, "misma marca y subgrupo");
            StringAssert.Contains(body, "puede pedirlo desde la tienda o a Lidia, como siempre");
            A.CallTo(() => repositorio.RegistrarEnvio(A<string>._, A<RecordatorioReposicionClienteDTO>._, A<string>._, A<DateTime>._,
                A<int?>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarTexto(A<string>._, A<DateTime>._, A<TextoRecordatorioReposicion>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_EnSombra_SeparaLosQueSeEnviarianDeLosQueQuedanFueraPorVendedor()
        {
            Modo("Sombra");

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            CollectionAssert.AreEqual(new[] { "15191", "15250" }, ejecucion.Plan.Enviar.Select(c => c.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "15200" }, ejecucion.Plan.FueraPorVendedor.Select(c => c.Cliente).ToArray());
            string body = WebUtility.HtmlDecode(Resumen().Body);
            StringAssert.Contains(body, "Se les escribiría (2 correos");
            StringAssert.Contains(body, "Fuera por vendedor (1)");
            StringAssert.Contains(Resumen().Subject, "2 se enviarían, 1 fuera por vendedor");
        }

        [TestMethod]
        public async Task Procesar_SiFallanLosEnlacesDeLaTienda_ElCorreoSombraSaleIgual()
        {
            Modo("Sombra");

            _ = await Procesar(c => throw new Exception("PrestaShop no responde"));

            Assert.AreEqual(1, enviados.Count);
        }

        [TestMethod]
        public async Task Procesar_EnSombra_LaMuestraEnsenaElTextoDeLaSemana()
        {
            Modo("Sombra");
            var generador = A.Fake<IGeneradorTextoRecordatorioReposicion>();
            A.CallTo(() => generador.Generar(HOY)).Returns(TextoIa());

            _ = await Procesar(generador: generador);

            string body = WebUtility.HtmlDecode(Resumen().Body);
            StringAssert.Contains(body, "Un aviso por si acaso: puede que pronto se le termine este producto.");
            StringAssert.Contains(body, "Asunto:</b> Por si acaso: crema <hidratante> 500 ml y su servicio");
            StringAssert.Contains(body, "ia-2026-09-24");
        }

        [TestMethod]
        public void ConstruirCorreoSombra_SinDestinatarios_VaAInformatica()
        {
            using (MailMessage mail = RecordatorioReposicionJobsService.ConstruirCorreoSombra(
                new EjecucionRecordatorioReposicionDTO { Resultado = Resultado() }, " ; "))
            {
                Assert.AreEqual(Constantes.Correos.INFORMATICA, mail.To.Single().Address);
            }
        }

        [TestMethod]
        public void MotivoResumido_QuitaElProductoDelFinal()
        {
            Assert.AreEqual("Después ha comprado un producto equivalente (misma marca y subgrupo)",
                RecordatorioReposicionJobsService.MotivoResumido(CalculadoraReposicion.MOTIVO_SUSTITUTO + "X"));
            Assert.AreEqual("Producto sin stock disponible",
                RecordatorioReposicionJobsService.MotivoResumido(CalculadoraReposicion.MOTIVO_SIN_STOCK));
            Assert.AreEqual("Ya se le recordó después de su última compra",
                RecordatorioReposicionJobsService.MotivoResumido(ReglasEnvioRecordatorioReposicion.MOTIVO_YA_RECORDADO + "01/09/2026"));
        }

        #endregion

        #region Activo

        [TestMethod]
        public async Task Procesar_EnActivo_EscribeSoloALosDeLosVendedoresDeLaListaYLosRegistra()
        {
            Modo("Activo");

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Activo, ejecucion.Modo);
            CollectionAssert.AreEquivalent(new[] { "15191@cliente.es", "15250@cliente.es" },
                CorreosAClientes().Select(m => m.To.Single().Address).ToList());
            Assert.IsFalse(enviados.Any(m => m.To.Any(t => t.Address == "15200@cliente.es")), "XYZ no está en la lista");
            Assert.IsFalse(enviados.Any(m => m.To.Any(t => t.Address == "15300@cliente.es")), "El grupo de control nunca lo recibe");
            CollectionAssert.AreEquivalent(new[] { "15191", "15250" },
                registrados.Where(r => r.Grupo == RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO).Select(r => r.Cliente).ToList());
            CollectionAssert.AreEqual(new[] { "15300" },
                registrados.Where(r => r.Grupo == RepositorioRecordatorioReposicionSql.GRUPO_CONTROL).Select(r => r.Cliente).ToList());
            Assert.IsTrue(registrados.All(r => r.Version == TextoRecordatorioReposicion.VERSION_FIJA && r.TextoId == 77));
        }

        [TestMethod]
        public async Task Procesar_EnActivo_ElCorreoAlClienteSaleDelBuzonDePosventaYContestaAhi()
        {
            Modo("Activo");

            _ = await Procesar();

            MailMessage mail = CorreosAClientes().First(m => m.To.Single().Address == "15191@cliente.es");
            Assert.AreEqual(BUZON, mail.From.Address);
            Assert.AreEqual(BUZON, mail.ReplyToList.Single().Address);
            Assert.AreEqual(0, mail.CC.Count);
            Assert.AreEqual("carlosadrian@nuevavision.es", mail.Bcc.Single().Address, "Carlos va en copia oculta de cada correo a un cliente");
            Assert.AreEqual("Que no le falte crema <hidratante> 500 ml en mitad de un servicio", mail.Subject);
            string body = WebUtility.HtmlDecode(mail.Body);
            StringAssert.Contains(body, "Hola:");
            StringAssert.Contains(body, "se le acabe este producto. Preferimos avisarle con tiempo");
            StringAssert.Contains(body, "(lo pidió por última vez el 15 de agosto)");
            StringAssert.Contains(body, "o a Lidia, como siempre.");
            StringAssert.Contains(body, PlantillaRecordatorioReposicion.FRASE_BAJA);
            StringAssert.Contains(body, "El equipo de Nueva Visión");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_ElResumenDiceQueSeHaEnviadoYQueSeHaQuedadoFuera()
        {
            Modo("Activo");

            _ = await Procesar();

            MailMessage resumen = Resumen();
            StringAssert.StartsWith(resumen.Subject, "[Activo]");
            StringAssert.Contains(resumen.Subject, "2 enviados, 1 fuera por vendedor");
            string body = WebUtility.HtmlDecode(resumen.Body);
            StringAssert.Contains(body, "Se ha escrito de verdad a 2 clientes");
            StringAssert.Contains(body, "Enviados de verdad (2 correos");
            StringAssert.Contains(body, "Fuera por vendedor (1)");
            StringAssert.Contains(body, "15200");
            StringAssert.Contains(body, "Registrados para medir (vendedor en la lista): 1.");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SinFilaDeExcluidosDePeluqueria_ElDeIFNoRecibeNiVaAlControl()
        {
            Modo("Activo");
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_EXCLUIR_VENDEDORES_PELUQUERIA, null);
            DependenciasRecordatorioReposicion d = Dependencias();
            d.Calcular = (consumibles, hoy) =>
            {
                ResultadoRecordatorioReposicionDTO r = Resultado();
                r.SeAvisarian.Single(c => c.Cliente == "15191").VendedorPeluqueria = "IF";
                r.SeAvisarian.Single(c => c.Cliente == "15250").VendedorPeluqueria = "JM";
                r.GrupoControl.Single().VendedorPeluqueria = "IF";
                return Task.FromResult(r);
            };

            EjecucionRecordatorioReposicionDTO ejecucion = await RecordatorioReposicionJobsService.Procesar(d);

            CollectionAssert.AreEqual(new[] { "15250@cliente.es" }, CorreosAClientes().Select(m => m.To.Single().Address).ToArray());
            CollectionAssert.AreEqual(new[] { "15191" }, ejecucion.Plan.FueraPorVendedorPeluqueria.Select(c => c.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "15250" }, registrados.Select(r => r.Cliente).ToArray(), "El 15300 (IF) tampoco se apunta al control");
            MailMessage resumen = Resumen();
            StringAssert.Contains(resumen.Subject, "1 fuera por vendedor de peluquería");
            StringAssert.Contains(WebUtility.HtmlDecode(resumen.Body), "Fuera por vendedor de peluquería (1)");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SinFilaDeVendedores_NoSeEscribeANadie()
        {
            Modo("Activo");
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_VENDEDORES, null);

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(0, CorreosAClientes().Count);
            Assert.AreEqual(3, ejecucion.Plan.FueraPorVendedor.Count);
            Assert.AreEqual(0, registrados.Count, "Tampoco se apunta el grupo de control");
            Assert.AreEqual(1, enviados.Count, "El resumen sí sale");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SinTablasDeRegistro_NoEscribeANadieYAvisa()
        {
            Modo("Activo");
            A.CallTo(() => repositorio.ExistenTablas()).Returns(false);

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(ModoRecordatorioReposicion.Sombra, ejecucion.Modo);
            Assert.AreEqual(ModoRecordatorioReposicion.Activo, ejecucion.ModoParametro);
            Assert.AreEqual(0, CorreosAClientes().Count);
            Assert.AreEqual(0, registrados.Count);
            StringAssert.Contains(WebUtility.HtmlDecode(Resumen().Body), "NO se ha escrito a ningún cliente");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SinRepositorio_NoEscribeANadie()
        {
            Modo("Activo");

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar(conRepositorio: false);

            Assert.AreEqual(ModoRecordatorioReposicion.Sombra, ejecucion.Modo);
            Assert.AreEqual(0, CorreosAClientes().Count);
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SiFallaElSmtpDeUnCliente_NoSeRegistraYLosDemasSalen()
        {
            Modo("Activo");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To.Any(t => t.Address == "15191@cliente.es"))))
                .Returns(false);

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            CollectionAssert.AreEqual(new[] { "15191" }, ejecucion.Plan.Fallidos.Select(c => c.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "15250" }, ejecucion.Plan.Enviados.Select(c => c.Cliente).ToArray());
            Assert.IsFalse(registrados.Any(r => r.Cliente == "15191"));
            StringAssert.Contains(WebUtility.HtmlDecode(Resumen().Body), "Ha fallado el envío (1)");
        }

        [TestMethod]
        public async Task Procesar_EnActivo_RespetaLasBajasYLoYaRecordado()
        {
            Modo("Activo");
            _ = bajas.Emails.Add("15191@CLIENTE.ES");
            previos.Add(new EnvioPrevioReposicion
            {
                Cliente = "15250", Producto = "CREMA500", Fecha = HOY.AddDays(-20), Grupo = RepositorioRecordatorioReposicionSql.GRUPO_CONTROL
            });

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            Assert.AreEqual(0, CorreosAClientes().Count);
            Assert.IsTrue(ejecucion.Resultado.Descartes.Any(d => d.Cliente == "15191" && d.Motivo == ReglasEnvioRecordatorioReposicion.MOTIVO_BAJA));
            Assert.IsTrue(ejecucion.Resultado.Descartes.Any(d => d.Cliente == "15250"
                && d.Motivo.StartsWith(ReglasEnvioRecordatorioReposicion.MOTIVO_YA_RECORDADO)));
        }

        [TestMethod]
        public async Task Procesar_EnActivo_LaBajaDeOtroTipoNoQuitaElRecordatorioYLaDeEsteSi()
        {
            Modo("Activo");
            bajas.Agregar("15191", null, TiposCorreoCliente.VIDEOS_COMPRAS);           // solo los vídeos de los sábados
            bajas.Agregar("15250", null, TiposCorreoCliente.RECORDATORIO_REPOSICION);  // solo este aviso

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar();

            CollectionAssert.AreEqual(new[] { "15191@cliente.es" }, CorreosAClientes().Select(m => m.To.Single().Address).ToList());
            Assert.IsTrue(ejecucion.Resultado.Descartes.Any(d => d.Cliente == "15250" && d.Motivo == ReglasEnvioRecordatorioReposicion.MOTIVO_BAJA));
        }

        [TestMethod]
        public void ConstruirCorreoCliente_SinCopiaOculta_NoLaPone()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
            using (MailMessage mail = RecordatorioReposicionJobsService.ConstruirCorreoCliente(cliente, null, BUZON, HOY, copiaOculta: null))
            {
                Assert.AreEqual(0, mail.Bcc.Count);
            }
        }

        [TestMethod]
        public async Task Procesar_EnActivo_ConElTextoDeOpenAI_LoUsaYGuardaSuVersion()
        {
            Modo("Activo");
            var generador = A.Fake<IGeneradorTextoRecordatorioReposicion>();
            A.CallTo(() => generador.Generar(HOY)).Returns(TextoIa());

            _ = await Procesar(generador: generador);

            MailMessage mail = CorreosAClientes().First(m => m.To.Single().Address == "15250@cliente.es");
            Assert.AreEqual("Por si acaso: crema <hidratante> 500 ml y su servicio", mail.Subject);
            StringAssert.Contains(WebUtility.HtmlDecode(mail.Body), "Puede pedírselo a nuestro equipo, como siempre.");
            StringAssert.Contains(WebUtility.HtmlDecode(mail.Body), PlantillaRecordatorioReposicion.FRASE_BAJA, "La baja la pone siempre el código");
            Assert.IsTrue(registrados.All(r => r.Version == "ia-2026-09-24"));
            A.CallTo(() => repositorio.GuardarTexto("1", HOY, A<TextoRecordatorioReposicion>.That.Matches(t => t.Version == "ia-2026-09-24")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Procesar_EnActivo_SiElGeneradorRevienta_SaleConLaFija()
        {
            Modo("Activo");
            var generador = A.Fake<IGeneradorTextoRecordatorioReposicion>();
            A.CallTo(() => generador.Generar(A<DateTime>._)).Throws(new Exception("sin red"));

            EjecucionRecordatorioReposicionDTO ejecucion = await Procesar(generador: generador);

            Assert.AreEqual(TextoRecordatorioReposicion.ORIGEN_FIJA, ejecucion.Texto.Origen);
            Assert.AreEqual(2, CorreosAClientes().Count);
        }

        [TestMethod]
        public async Task Procesar_EnActivo_ConFoto_LaMiniaturaVaEnlazadaALaTienda()
        {
            Modo("Activo");

            _ = await Procesar(c =>
            {
                c.Productos[0].EnlaceTienda = "https://tienda.es/crema";
                c.Productos[0].UrlFoto = "https://tienda.es/img/crema.jpg";
                return Task.CompletedTask;
            });

            string body = CorreosAClientes().First().Body;
            StringAssert.Contains(body, "<img src='https://tienda.es/img/crema.jpg'");
            StringAssert.Contains(body, "<a href='https://tienda.es/crema?" + PlantillaRecordatorioReposicion.UTM.Replace("&", "&amp;") +
                "' style='text-decoration: none;'><img");
        }

        private static TextoRecordatorioReposicion TextoIa() => new TextoRecordatorioReposicion
        {
            Asunto = "Por si acaso: {PRODUCTO} y su servicio",
            Entrada = "Un aviso por si acaso: puede que pronto se le termine {PRODUCTOS}.",
            Cierre = "Si le queda, no haga nada. Puede pedírselo a {VENDEDOR}, como siempre.",
            Origen = TextoRecordatorioReposicion.ORIGEN_OPENAI,
            Version = "ia-2026-09-24"
        };

        #endregion

        #region Plantilla y fotos

        [TestMethod]
        public void Plantilla_DeUstedConEnlaceYVendedor()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
            cliente.Productos[0].EnlaceTienda = "https://tienda.es/crema?utm_source=otra";

            string html = WebUtility.HtmlDecode(PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY));

            StringAssert.Contains(html, "Le escribimos con un pequeño aviso: es posible que dentro de poco se le acabe este producto.");
            StringAssert.Contains(html, "para que no tenga que descubrirlo con un cliente ya delante:");
            StringAssert.Contains(html, "https://tienda.es/crema?" + PlantillaRecordatorioReposicion.UTM);
            Assert.IsFalse(html.Contains("utm_source=otra"));
            StringAssert.Contains(html, "Si todavía tiene de sobra, olvide este correo. Y si le hace falta, puede pedirlo desde la tienda o a Lidia, como siempre.");
            StringAssert.Contains(html, "(lo pidió por última vez el 15 de agosto)");
            foreach (string vetada in new[] { " tu ", " te ", "puedes", "suele", "camilla", "mesa", "tocador" })
            {
                Assert.IsFalse(html.IndexOf(vetada, StringComparison.OrdinalIgnoreCase) >= 0, vetada);
            }
        }

        [TestMethod]
        public void Plantilla_VariosProductos_AsuntoYAlgoMasYAlgunoDeEstos()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
            cliente.Productos.Add(Producto("15191", "LACA", "LACA FUERTE"));

            Assert.AreEqual("Que no le falte crema <hidratante> 500 ml y algo más en mitad de un servicio",
                PlantillaRecordatorioReposicion.Asunto(cliente));
            StringAssert.Contains(PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY), "alguno de estos productos");
        }

        [TestMethod]
        public void Plantilla_SinVendedor_NuestroEquipo()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15250", null, null);
            StringAssert.Contains(WebUtility.HtmlDecode(PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY)),
                "o a nuestro equipo, como siempre.");
        }

        [TestMethod]
        public void Plantilla_CompraDeOtroAnio_LlevaElAnio()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
            cliente.Productos[0].UltimaCompra = new DateTime(2025, 12, 3);

            StringAssert.Contains(WebUtility.HtmlDecode(PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY)),
                "(lo pidió por última vez el 3 de diciembre de 2025)");
        }

        [TestMethod]
        public void Plantilla_ConFoto_MiniaturaJuntoAlNombre()
        {
            RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
            cliente.Productos[0].UrlFoto = "https://www.tienda.es/12-home_default/crema.jpg";
            cliente.Productos[0].EnlaceTienda = "https://www.tienda.es/crema";

            string html = PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY);

            StringAssert.Contains(html, "<img src='https://www.tienda.es/12-home_default/crema.jpg' width='64' height='64'");
            StringAssert.Contains(html, "alt='CREMA &lt;HIDRATANTE&gt; 500 ML'");
        }

        [TestMethod]
        public void Plantilla_SinFoto_SoloElNombre()
        {
            foreach (string sinFoto in new[] { null, "", "https://", "no es una url" })
            {
                RecordatorioReposicionClienteDTO cliente = Cliente("15191", "LM", "Lidia");
                cliente.Productos[0].UrlFoto = sinFoto;
                cliente.Productos[0].EnlaceTienda = "https://www.tienda.es/crema";

                string html = PlantillaRecordatorioReposicion.CuerpoHtml(cliente, null, HOY);

                Assert.IsFalse(html.Contains("<img"), sinFoto ?? "null");
                StringAssert.Contains(html, "<b>CREMA &lt;HIDRATANTE&gt; 500 ML</b>");
            }
        }

        [TestMethod]
        public void NormalizarUrlFoto_RutaImagenSinFotoDevuelveSoloElEsquema()
        {
            Assert.IsNull(PlantillaRecordatorioReposicion.NormalizarUrlFoto("https://"));
            Assert.IsNull(PlantillaRecordatorioReposicion.NormalizarUrlFoto("https://www.tienda.es/"));
            Assert.IsNull(PlantillaRecordatorioReposicion.NormalizarUrlFoto("ftp://www.tienda.es/a.jpg"));
            Assert.AreEqual("https://www.tienda.es/a.jpg", PlantillaRecordatorioReposicion.NormalizarUrlFoto(" https://www.tienda.es/a.jpg "));
        }

        [TestMethod]
        public async Task Rellenador_UnaLlamadaPorProductoParaTodaLaPasada_YSinFotoNull()
        {
            int enlaces = 0, fotos = 0;
            Func<RecordatorioReposicionClienteDTO, Task> rellenar = RecordatorioReposicionJobsService.CrearRellenadorProductos(
                p => { enlaces++; return Task.FromResult("https://tienda.es/" + p); },
                p => { fotos++; return Task.FromResult(p == "CREMA500" ? "https://tienda.es/img/" + p + ".jpg" : "https://"); });
            RecordatorioReposicionClienteDTO uno = Cliente("1", "LM", "Lidia");
            RecordatorioReposicionClienteDTO dos = Cliente("2", "LM", "Lidia");
            dos.Productos.Add(Producto("2", "LACA", "LACA"));

            await rellenar(uno);
            await rellenar(dos);

            Assert.AreEqual(2, enlaces);
            Assert.AreEqual(2, fotos);
            Assert.AreEqual("https://tienda.es/img/CREMA500.jpg", dos.Productos[0].UrlFoto);
            Assert.IsNull(dos.Productos[1].UrlFoto);
            Assert.AreEqual("https://tienda.es/LACA", dos.Productos[1].EnlaceTienda);
        }

        [TestMethod]
        public async Task Rellenador_SiFallaLaTienda_SinEnlaceDeFichaNiFoto()
        {
            Func<RecordatorioReposicionClienteDTO, Task> rellenar = RecordatorioReposicionJobsService.CrearRellenadorProductos(
                p => throw new Exception("caída"), p => throw new Exception("caída"));
            RecordatorioReposicionClienteDTO uno = Cliente("1", "LM", "Lidia");

            await rellenar(uno);

            Assert.IsNull(uno.Productos[0].UrlFoto);
            Assert.IsNotNull(uno.Productos[0].EnlaceTienda, "Sin ficha, el enlace de búsqueda de la tienda");
        }

        #endregion

        #region Controller

        [TestMethod]
        public async Task Controller_GetReposicion_CalculaEnSecoSinMandarNada()
        {
            var selector = A.Fake<ISelectorRecordatoriosReposicion>();
            A.CallTo(() => selector.Calcular("1", new DateTime(2026, 9, 17), A<string>._)).Returns(Resultado());
            var controller = new RecordatorioReposicionController(() => selector, lector, correo);

            var respuesta = await controller.GetReposicion("1", "2026-09-17") as OkNegotiatedContentResult<ResultadoRecordatorioReposicionDTO>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(3, respuesta.Content.Correos);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controller_GetReposicion_FechaMalFormada_BadRequest()
        {
            var controller = new RecordatorioReposicionController(() => A.Fake<ISelectorRecordatoriosReposicion>(), lector, correo);
            Assert.IsInstanceOfType(await controller.GetReposicion("1", "17/09/2026"), typeof(BadRequestErrorMessageResult));
        }

        #endregion
    }
}
