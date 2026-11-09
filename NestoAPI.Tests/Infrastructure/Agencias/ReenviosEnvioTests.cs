using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#699 (corte 2, «Volver a enviar»): la agencia devuelve un envío (o lo deja incidentado) y el almacén o el
    /// vendedor deciden si el cliente lo sigue queriendo. Reenviar = un envío nuevo del mismo pedido, clonado, en curso y sin
    /// etiqueta (sin abono, stock, picking ni factura); Abonar = solo la decisión. Una decisión por envío.
    /// </summary>
    [TestClass]
    public class ReenviosEnvioTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 4);
        private const string USUARIO = @"NUEVAVISION\Alfredo";

        private DatosFalsos datos;
        private IPlazaYCodigoBarrasAgencia plazaGls;
        private ServicioReenviosEnvio servicio;

        [TestInitialize]
        public void Setup()
        {
            datos = new DatosFalsos();
            plazaGls = A.Fake<IPlazaYCodigoBarrasAgencia>();
            A.CallTo(() => plazaGls.Agencias).Returns(new[] { Constantes.Agencias.AGENCIA_GLS });
            A.CallTo(() => plazaGls.PlazaConsultaLaAgencia).Returns(true);
            A.CallTo(() => plazaGls.CalcularPlazaAsync(A<string>._, A<int>._))
                .ReturnsLazily((string cp, int pais) => Task.FromResult(new PlazaAgencia { Nemonico = "P" + cp, Nombre = "Plaza " + cp }));
            A.CallTo(() => plazaGls.CalcularCodigoBarras(A<EnviosAgencia>._, A<string>._))
                .ReturnsLazily((EnviosAgencia e, string prefijo) => prefijo + e.Numero);
            var plazas = new RegistroPlazasAgencias(new[] { plazaGls, PlazaYCodigoBarrasFija.Ctt() });
            servicio = new ServicioReenviosEnvio(datos, plazas, () => HOY);
        }

        private static EnviosAgencia Devuelto(int numero = 251300, int agencia = Constantes.Agencias.AGENCIA_CTT,
            short estado = Constantes.Agencias.ESTADO_DEVUELTO) => new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Agencia = agencia,
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 925137,
                Estado = estado,
                DetalleEstado = "DEVOLUCIÓN: AUSENTE",
                Fecha = new DateTime(2026, 10, 28),
                FechaEntrega = new DateTime(2026, 10, 29),
                Servicio = 63,
                Horario = 2,
                Bultos = 3,
                Retorno = 1,
                Peso = 12.5M,
                Nombre = "Peluquería Ejemplo",
                Direccion = "Calle Vieja 1",
                CodPostal = "28001",
                Poblacion = "Madrid",
                Provincia = "Madrid",
                Pais = 34,
                Telefono = "910000000",
                Movil = "600000000",
                Email = "cliente@ejemplo.es",
                Observaciones = "Llamar antes",
                Atencion = "Marta",
                Reembolso = 147.09M,
                Vendedor = "SAN",
                ImporteGasto = 6.5M,
                ImporteAsegurado = 0,
                CodigoBarras = "0082800082909720018884",
                Nemonico = "CT",
                NombrePlaza = "CTT Express",
                Usuario = @"NUEVAVISION\almacen"
            };

        // ===== Reenviar =====

        [TestMethod]
        public async Task Reenviar_Devuelto_ClonaElEnvioEnCursoSinEtiqueta()
        {
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Correcto, r.Tipo, r.Mensaje);
            EnviosAgencia nuevo = datos.Anadidos.Single();
            Assert.AreNotEqual(251300, nuevo.Numero);
            Assert.AreEqual("1  ", nuevo.Empresa);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_CTT, nuevo.Agencia);
            Assert.AreEqual("15191     ", nuevo.Cliente);
            Assert.AreEqual("0  ", nuevo.Contacto);
            Assert.AreEqual(925137, nuevo.Pedido);
            Assert.AreEqual((short)3, nuevo.Bultos);
            Assert.AreEqual(12.5M, nuevo.Peso);
            Assert.AreEqual((short)63, nuevo.Servicio);
            Assert.AreEqual((short)2, nuevo.Horario);
            Assert.AreEqual((short)1, nuevo.Retorno);
            Assert.AreEqual(147.09M, nuevo.Reembolso);
            Assert.AreEqual("Peluquería Ejemplo", nuevo.Nombre);
            Assert.AreEqual("SAN", nuevo.Vendedor);
            Assert.AreEqual("Calle Vieja 1", nuevo.Direccion);
            Assert.AreEqual("28001", nuevo.CodPostal);
            Assert.AreEqual("910000000", nuevo.Telefono);
            Assert.AreEqual("cliente@ejemplo.es", nuevo.Email);
            Assert.AreEqual("Marta", nuevo.Atencion);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, nuevo.Estado);
            Assert.AreEqual(HOY, nuevo.Fecha);
            Assert.AreEqual(HOY.AddDays(1), nuevo.FechaEntrega);
            Assert.IsNull(nuevo.DetalleEstado);
            Assert.AreEqual(string.Empty, nuevo.CodigoBarras, "CTT: el albarán lo da /Tramitar, como cualquier envío en curso");
            Assert.AreEqual("CT", nuevo.Nemonico, "Mismo CP: la plaza del devuelto");
            Assert.AreEqual(USUARIO, nuevo.Usuario);

            RegistroReenvioEnvio fila = datos.Repo.Filas[251300];
            Assert.AreEqual(DecisionesReenvio.REENVIAR, fila.Decision);
            Assert.AreEqual(nuevo.Numero, fila.EnvioNuevo);
            Assert.IsFalse(fila.IncluirPortes);
            Assert.AreEqual(USUARIO, fila.Usuario);
            Assert.AreEqual(nuevo.Numero, r.Decision.EnvioNuevo);
            Assert.AreEqual($"Envío nuevo {nuevo.Numero} creado: imprima la etiqueta cuando llegue la caja.", r.Decision.Mensaje);
            Assert.AreEqual(0, datos.PortesAnadidos.Count, "Por defecto los portes del reenvío nos los comemos");
            Assert.IsFalse(r.Decision.PortesIncluidos);
        }

        [TestMethod]
        public async Task Reenviar_ConLaDireccionCorregida_LaUsaYRecalculaLaPlaza()
        {
            datos.Envios[251300] = Devuelto(agencia: Constantes.Agencias.AGENCIA_GLS);
            datos.Agencia = new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, PrefijoCodigoBarras = "61771" };

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO
            {
                Direccion = " Calle Nueva 7 ",
                CodPostal = "28100",
                Poblacion = "Alcobendas",
                Telefono = "911111111",
                Observaciones = "Dejar en la portería"
            }, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Correcto, r.Tipo, r.Mensaje);
            EnviosAgencia nuevo = datos.Anadidos.Single();
            Assert.AreEqual("Calle Nueva 7", nuevo.Direccion);
            Assert.AreEqual("28100", nuevo.CodPostal);
            Assert.AreEqual("Alcobendas", nuevo.Poblacion);
            Assert.AreEqual("Madrid", nuevo.Provincia, "Lo que no viene se copia");
            Assert.AreEqual("911111111", nuevo.Telefono);
            Assert.AreEqual("600000000", nuevo.Movil);
            Assert.AreEqual("Dejar en la portería", nuevo.Observaciones);
            Assert.AreEqual("P28100", nuevo.Nemonico, "Otro CP: la plaza se vuelve a pedir");
            Assert.AreEqual("61771" + nuevo.Numero, nuevo.CodigoBarras, "GLS: el código se calcula con el número del envío nuevo");
            Assert.AreEqual("61771" + nuevo.Numero, r.Decision.CodigoBarras);
            RegistroReenvioEnvio fila = datos.Repo.Filas[251300];
            Assert.AreEqual("Calle Nueva 7", fila.Direccion);
            Assert.AreEqual("28100", fila.CodPostal);
            Assert.AreEqual("911111111", fila.Telefono);
        }

        [TestMethod]
        public async Task Reenviar_Incidentado_TambienSePuede()
        {
            datos.Envios[251300] = Devuelto(estado: Constantes.Agencias.ESTADO_INCIDENTADO);

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, null, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Correcto, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, datos.Anadidos.Count);
        }

        [TestMethod]
        public async Task Reenviar_ConPortes_AnadeLaLineaYLoApunta()
        {
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO { IncluirPortes = true }, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Correcto, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, datos.PortesAnadidos.Count);
            Assert.AreEqual(925137, datos.PortesAnadidos[0].Pedido);
            Assert.IsTrue(datos.Repo.Filas[251300].IncluirPortes);
            Assert.IsTrue(r.Decision.PortesIncluidos);
        }

        [TestMethod]
        public async Task Reenviar_DevueltoAlQueElSeguimientoQuitoElReembolso_LoRecupera()
        {
            // Sugerencia 544: al pasar a devuelto se le quita el reembolso; el reenvío tiene que cobrarlo.
            EnviosAgencia devuelto = Devuelto();
            devuelto.Reembolso = 0;
            datos.Envios[251300] = devuelto;
            datos.ReembolsoQuitado = 147.09M;

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(147.09M, datos.Anadidos.Single().Reembolso);
            Assert.AreEqual(147.09M, r.Decision.Reembolso);
            Assert.IsTrue(r.Decision.Avisos.Any(a => a.Contains("147,09")), string.Join(" | ", r.Decision.Avisos));
        }

        [TestMethod]
        public async Task Reenviar_SinTabla_400YNoCreaNada()
        {
            datos.Repo.Tabla = false;
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "Issue699_EnviosAgenciaReenvios.sql");
            Assert.AreEqual(0, datos.Anadidos.Count);
        }

        [TestMethod]
        public async Task Reenviar_NoExiste_NoEncontrado()
        {
            ResultadoReenvio r = await servicio.ReenviarAsync(1, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.NoEncontrado, r.Tipo);
        }

        [TestMethod]
        public async Task Reenviar_YaReenviado_409YNoCreaOtro()
        {
            datos.Envios[251300] = Devuelto();
            _ = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);
            int primero = datos.Anadidos.Single().Numero;

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Conflicto, r.Tipo);
            StringAssert.Contains(r.Mensaje, $"ya se volvió a enviar en el envío {primero}");
            Assert.AreEqual(1, datos.Anadidos.Count);
        }

        [TestMethod]
        public async Task Reenviar_MarcadoParaAbonar_409YRemiteACopiarFactura()
        {
            datos.Envios[251300] = Devuelto();
            _ = await servicio.AbonarAsync(251300, USUARIO);

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Conflicto, r.Tipo);
            StringAssert.Contains(r.Mensaje, "Copiar factura");
            Assert.AreEqual(0, datos.Anadidos.Count);
        }

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_ENTREGADO, "entregado")]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, "no está devuelto ni incidentado")]
        [DataRow(Constantes.Agencias.ESTADO_CERRADO_A_MANO, "no está devuelto ni incidentado")]
        public async Task Reenviar_NiDevueltoNiIncidentado_409(short estado, string texto)
        {
            datos.Envios[251300] = Devuelto(estado: estado);

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Conflicto, r.Tipo);
            StringAssert.Contains(r.Mensaje, texto);
            Assert.AreEqual(0, datos.Anadidos.Count);
        }

        [TestMethod]
        public async Task Reenviar_OtraSesionDecideALaVez_409YSeDeshace()
        {
            datos.Envios[251300] = Devuelto();
            datos.Repo.AlInsertar = () => datos.Repo.Filas[251300] = new RegistroReenvioEnvio
            {
                EnvioOrigen = 251300, Decision = DecisionesReenvio.ABONAR, Usuario = "otro"
            };

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO(), USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Conflicto, r.Tipo);
            Assert.IsTrue(datos.TransaccionDeshecha, "El envío nuevo no se queda grabado");
        }

        [TestMethod]
        public async Task Reenviar_TelefonoDemasiadoLargo_400()
        {
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.ReenviarAsync(251300, new ReenviarEnvioDTO { Telefono = "1234567890123456" }, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "teléfono");
        }

        // ===== Abonar =====

        [TestMethod]
        public async Task Abonar_SoloGrabaLaDecision()
        {
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.AbonarAsync(251300, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Correcto, r.Tipo, r.Mensaje);
            RegistroReenvioEnvio fila = datos.Repo.Filas[251300];
            Assert.AreEqual(DecisionesReenvio.ABONAR, fila.Decision);
            Assert.IsNull(fila.EnvioNuevo);
            Assert.AreEqual(0, datos.Anadidos.Count);
            Assert.AreEqual("Marcado para abonar: hágalo con Copiar factura (abono + rehacer si hiciera falta).", r.Decision.Mensaje);
        }

        [TestMethod]
        public async Task Abonar_DosVeces_409()
        {
            datos.Envios[251300] = Devuelto();
            _ = await servicio.AbonarAsync(251300, USUARIO);

            ResultadoReenvio r = await servicio.AbonarAsync(251300, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Conflicto, r.Tipo);
        }

        [TestMethod]
        public async Task Abonar_SinTabla_400()
        {
            datos.Repo.Tabla = false;
            datos.Envios[251300] = Devuelto();

            ResultadoReenvio r = await servicio.AbonarAsync(251300, USUARIO);

            Assert.AreEqual(TipoResultadoReenvio.Invalido, r.Tipo);
        }

        // ===== Listado =====

        [TestMethod]
        public async Task Devueltos_ConLaDecision_PrimeroLosSinDecidir()
        {
            datos.Listado = new List<EnvioAgenciaListadoDTO>
            {
                new EnvioAgenciaListadoDTO { Numero = 1, Fecha = new DateTime(2026, 11, 3), Estado = 4, Pedido = 11 },
                new EnvioAgenciaListadoDTO { Numero = 2, Fecha = new DateTime(2026, 11, 1), Estado = 4, Pedido = 22 },
                new EnvioAgenciaListadoDTO { Numero = 3, Fecha = new DateTime(2026, 10, 30), Estado = 3, Pedido = 33 },
                new EnvioAgenciaListadoDTO { Numero = 4, Fecha = new DateTime(2026, 10, 29), Estado = 4, Pedido = 44 }
            };
            datos.Repo.Filas[1] = new RegistroReenvioEnvio { EnvioOrigen = 1, Decision = DecisionesReenvio.REENVIAR, EnvioNuevo = 100, Usuario = "Alfredo", IncluirPortes = true };
            datos.Repo.Filas[3] = new RegistroReenvioEnvio { EnvioOrigen = 3, Decision = DecisionesReenvio.ABONAR, Usuario = "Santi" };
            datos.Repo.Filas[4] = new RegistroReenvioEnvio { EnvioOrigen = 4, Decision = DecisionesReenvio.REENVIAR, EnvioNuevo = 101 };
            datos.Estados[100] = Constantes.Agencias.ESTADO_EN_CURSO;
            datos.Estados[101] = Constantes.Agencias.ESTADO_TRAMITADO;

            List<EnvioDevueltoDTO> lista = await servicio.LeerDevueltosAsync("1");

            CollectionAssert.AreEqual(new[] { 2, 1, 3, 4 }, lista.Select(l => l.Numero).ToList());
            Assert.AreEqual(DecisionesReenvio.SIN_DECIDIR, lista[0].Decision);
            Assert.AreEqual(22, lista[0].Pedido, "Lleva los datos del listado de siempre");
            EnvioDevueltoDTO reenviado = lista[1];
            Assert.AreEqual(DecisionesReenvio.REENVIAR, reenviado.Decision);
            Assert.AreEqual(100, reenviado.EnvioNuevo);
            Assert.IsFalse(reenviado.EnvioNuevoConEtiqueta);
            Assert.IsTrue(reenviado.PortesIncluidos);
            Assert.AreEqual("Alfredo", reenviado.UsuarioDecision);
            Assert.AreEqual(DecisionesReenvio.ABONAR, lista[2].Decision);
            Assert.IsTrue(lista[3].EnvioNuevoConEtiqueta, "El envío nuevo ya está tramitado");
            Assert.AreEqual(HOY.AddDays(-ServicioReenviosEnvio.DIAS_LISTADO), datos.DesdeLeido);
        }

        [TestMethod]
        public async Task Devueltos_SinTabla_Null()
        {
            datos.Repo.Tabla = false;

            Assert.IsNull(await servicio.LeerDevueltosAsync("1"));
        }

        [DataTestMethod]
        [DataRow("147,09 €", 147.09)]
        [DataRow("1.147,09 €", 1147.09)]
        [DataRow("0,00 €", 0)]
        public void LeerImporteHistoria_ElFormatoDeModificarDatos(string valor, double esperado)
        {
            Assert.AreEqual((decimal)esperado, ServicioReenviosEnvio.LeerImporteHistoria(valor));
        }

        [DataTestMethod]
        [DataRow(@"NUEVAVISION\Alfredo", "Alfredo")]
        [DataRow("Alfredo", "Alfredo")]
        [DataRow(null, "")]
        public void UsuarioSinDominio_ParaLaLineaDePortes(string usuario, string esperado)
        {
            // CrearLineaVta le antepone el dominio: si no se quita, la línea saldría como NUEVAVISION\NUEVAVISION\Alfredo.
            Assert.AreEqual(esperado, DatosReenviosEnvioEF.UsuarioSinDominio(usuario));
        }

        // ===== El controlador =====

        private EnviosAgenciasController Controlador() => new EnviosAgenciasController(A.Fake<NVEntities>())
        {
            ServicioReenvios = servicio,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, USUARIO) }, "Bearer"))
        };

        [TestMethod]
        public async Task Controlador_Reenviar_DevuelveLaDecisionConElUsuarioDelToken()
        {
            datos.Envios[251300] = Devuelto();

            var r = await Controlador().Reenviar(251300, new ReenviarEnvioDTO()) as OkNegotiatedContentResult<DecisionReenvioDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(DecisionesReenvio.REENVIAR, r.Content.Decision);
            Assert.AreEqual(USUARIO, datos.Repo.Filas[251300].Usuario);
        }

        [TestMethod]
        public async Task Controlador_ReenviarEntregado_409()
        {
            datos.Envios[251300] = Devuelto(estado: Constantes.Agencias.ESTADO_ENTREGADO);

            var r = await Controlador().Reenviar(251300, new ReenviarEnvioDTO()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.Conflict, r.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_SinTabla_400EnLosTres()
        {
            datos.Repo.Tabla = false;
            datos.Envios[251300] = Devuelto();
            EnviosAgenciasController c = Controlador();

            Assert.IsInstanceOfType(await c.Reenviar(251300, new ReenviarEnvioDTO()), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await c.Abonar(251300), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await c.GetEnviosDevueltos("1"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Controlador_AbonarNoExiste_404()
        {
            Assert.IsInstanceOfType(await Controlador().Abonar(9), typeof(NotFoundResult));
        }

        // ===== El aviso al vendedor al pasar a devuelto =====

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_DEVUELTO, true)]
        [DataRow(Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_DEVUELTO, true)]
        [DataRow(Constantes.Agencias.ESTADO_DEVUELTO, Constantes.Agencias.ESTADO_DEVUELTO, false)]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_ENTREGADO, false)]
        public void PasaADevuelto_SoloEnLaTransicion(short anterior, short nuevo, bool esperado)
        {
            var envio = new EnviosAgencia { Estado = nuevo };
            Assert.AreEqual(esperado, AvisadorIncidenciasEnvio.PasaADevuelto(anterior, envio));
            Assert.AreEqual(esperado, AvisadorIncidenciasEnvio.HayQueAvisar(anterior, envio));
        }

        private static ComercialesPedido Comerciales() => new ComercialesPedido
        {
            Empresa = "1",
            Pedido = 925137,
            Cliente = "15191",
            NombreCliente = "Peluquería Ejemplo",
            Comerciales =
            {
                new ComercialPedido { Vendedor = "SAN", Nombre = "Santiago", Correo = "santiago@nuevavision.es", Usuarios = { "Santiago" } }
            }
        };

        [TestMethod]
        public void ComponerCorreoDevolucion_PreguntaSiLoSigueQueriendoYDondeContestar()
        {
            MailMessage mail = AvisadorIncidenciasEnvio.ComponerCorreoDevolucion(Devuelto(), Comerciales(), "CTT");

            Assert.AreEqual("Envío devuelto del pedido 925137 - c/ 15191 - Peluquería Ejemplo: ¿lo sigue queriendo el cliente?", mail.Subject);
            Assert.AreEqual("santiago@nuevavision.es", mail.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.LOGISTICA, mail.CC.Single().Address);
            StringAssert.Contains(mail.Body, "251300");
            StringAssert.Contains(mail.Body, WebUtility.HtmlEncode("DEVOLUCIÓN: AUSENTE"));
            StringAssert.Contains(mail.Body, "Contéstelo en Nesto › Agencias › Devueltos");
            // 09/11/26 (Carlos): los vendedores que solo usan NestoApp contestan a Logística (va en copia), no a Administración
            StringAssert.Contains(mail.Body, "Si no usa Nesto");
            StringAssert.Contains(mail.Body, "avise a Logística");
        }

        [TestMethod]
        public async Task AvisarAsync_Devuelto_CorreoYPushDeDevolucion()
        {
            var (avisador, correo, notificaciones) = Avisador(yaDecidido: false);

            await avisador.AvisarAsync(Devuelto());

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.Subject.StartsWith("Envío devuelto"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAVendedor("1", "SAN", A<NotificacionPushDTO>.That.Matches(n =>
                    n.Tipo == AvisadorIncidenciasEnvio.TIPO_NOTIFICACION_DEVOLUCION
                    && n.Datos["tipo"] == AvisadorIncidenciasEnvio.TIPO_NOTIFICACION_DEVOLUCION
                    && n.Datos["envio"] == "251300" && n.Datos["pedido"] == "925137")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AvisarAsync_DevueltoYaDecidido_NoPreguntaOtraVez()
        {
            var (avisador, correo, notificaciones) = Avisador(yaDecidido: true);

            await avisador.AvisarAsync(Devuelto());

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.EnviarAVendedor(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        private static (AvisadorIncidenciasEnvio, IServicioCorreoElectronico, IServicioNotificacionesPush) Avisador(bool yaDecidido)
        {
            NVEntities db = A.Fake<NVEntities>();
            var agencias = new List<AgenciaTransporte> { new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_CTT, Nombre = "CTT  " } }.AsQueryable();
            DbSet<AgenciaTransporte> fakeAgencias = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>());
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).Provider).Returns(agencias.Provider);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).Expression).Returns(agencias.Expression);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).ElementType).Returns(agencias.ElementType);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).GetEnumerator()).ReturnsLazily(() => agencias.GetEnumerator());
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeAgencias);
            var lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer(A<string>._, 925137)).Returns(Comerciales());
            var correo = A.Fake<IServicioCorreoElectronico>();
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorIncidenciasEnvio(db, lector, correo, notificaciones, _ => Task.FromResult(yaDecidido));
            return (avisador, correo, notificaciones);
        }

        // ===== NestoAPI#699: la agencia acaba entregando el envío que se decidió reenviar =====

        private readonly List<(int Origen, int? Nuevo, bool Anulado, RegistroReenvioEnvio Fila)> avisosEntrega =
            new List<(int, int?, bool, RegistroReenvioEnvio)>();

        private ReenvioEntregadoPorLaAgencia Entrega() => new ReenvioEntregadoPorLaAgencia(datos, (origen, fila, nuevo, anulado) =>
        {
            avisosEntrega.Add((origen.Numero, nuevo?.Numero, anulado, fila));
            return Task.CompletedTask;
        });

        private EnviosAgencia EntregadoConReenvio(short estadoNuevo, bool portes = false)
        {
            EnviosAgencia viejo = Devuelto(estado: Constantes.Agencias.ESTADO_ENTREGADO);
            datos.Envios[251300] = viejo;
            datos.Envios[300000] = new EnviosAgencia { Numero = 300000, Estado = estadoNuevo, Pedido = 925137, CodigoBarras = "ETQ300000" };
            datos.Repo.Filas[251300] = new RegistroReenvioEnvio
            {
                EnvioOrigen = 251300,
                Decision = DecisionesReenvio.REENVIAR,
                EnvioNuevo = 300000,
                IncluirPortes = portes,
                Usuario = "Alfredo",
                Fecha = HOY
            };
            return viejo;
        }

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_ENTREGADO, true)]
        [DataRow(Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_ENTREGADO, true)]
        [DataRow(Constantes.Agencias.ESTADO_CERRADO_A_MANO, Constantes.Agencias.ESTADO_ENTREGADO, true)]
        [DataRow(Constantes.Agencias.ESTADO_ENTREGADO, Constantes.Agencias.ESTADO_ENTREGADO, false)]
        [DataRow(Constantes.Agencias.ESTADO_DEVUELTO, Constantes.Agencias.ESTADO_ENTREGADO, false)]
        [DataRow(Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_TRAMITADO, false)]
        public void PasaAEntregado_SoloEnLaTransicionYNoDesdeDevuelto(short anterior, short nuevo, bool esperado)
        {
            Assert.AreEqual(esperado, ReenvioEntregadoPorLaAgencia.PasaAEntregado(anterior, new EnviosAgencia { Estado = nuevo }));
        }

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_EN_CURSO)]
        [DataRow(Constantes.Agencias.ESTADO_PENDIENTE)]
        public async Task EntregadoConReenvioSinEtiqueta_BorraElReenvioPasaLaFilaYAvisa(int estadoNuevo)
        {
            EnviosAgencia viejo = EntregadoConReenvio((short)estadoNuevo, portes: true);

            ResultadoReenvioEntregado r = await Entrega().ResolverAsync(viejo);

            Assert.AreEqual(ResultadoReenvioEntregado.ReenvioAnulado, r);
            Assert.IsFalse(datos.Envios.ContainsKey(300000), "El envío nuevo se borra");
            RegistroReenvioEnvio fila = datos.Repo.Filas[251300];
            Assert.AreEqual(DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA, fila.Decision);
            Assert.IsNull(fila.EnvioNuevo);
            Assert.AreEqual(300000, fila.EnvioAnulado);
            Assert.AreEqual("La agencia acabó entregándolo: se anula el reenvío 300000.", datos.Repo.HistoriaAnulacion);
            Assert.AreEqual(ReenvioEntregadoPorLaAgencia.USUARIO, datos.Repo.UsuarioAnulacion);
            var aviso = avisosEntrega.Single();
            Assert.AreEqual(251300, aviso.Origen);
            Assert.AreEqual(300000, aviso.Nuevo);
            Assert.IsTrue(aviso.Anulado);
            Assert.IsTrue(aviso.Fila.IncluirPortes);
        }

        [TestMethod]
        public async Task EntregadoConReenvioYaTramitado_NoTocaNadaYAvisaDeDosEnviosVivos()
        {
            EnviosAgencia viejo = EntregadoConReenvio(Constantes.Agencias.ESTADO_TRAMITADO);

            ResultadoReenvioEntregado r = await Entrega().ResolverAsync(viejo);

            Assert.AreEqual(ResultadoReenvioEntregado.DosEnviosVivos, r);
            Assert.IsTrue(datos.Envios.ContainsKey(300000));
            Assert.AreEqual(DecisionesReenvio.REENVIAR, datos.Repo.Filas[251300].Decision);
            Assert.AreEqual(300000, datos.Repo.Filas[251300].EnvioNuevo);
            Assert.AreEqual(0, datos.Repo.Anulaciones, "Ni se intenta anular");
            Assert.IsFalse(avisosEntrega.Single().Anulado);
        }

        [TestMethod]
        public async Task EntregadoConReenvio_LoTramitanALaVez_NoSeBorraYAvisaDeDosEnviosVivos()
        {
            EnviosAgencia viejo = EntregadoConReenvio(Constantes.Agencias.ESTADO_EN_CURSO);
            datos.Repo.ForzarResultado = ResultadoAnulacionReenvio.YaTramitado;

            ResultadoReenvioEntregado r = await Entrega().ResolverAsync(viejo);

            Assert.AreEqual(ResultadoReenvioEntregado.DosEnviosVivos, r);
            Assert.IsTrue(datos.Envios.ContainsKey(300000));
            Assert.IsFalse(avisosEntrega.Single().Anulado);
        }

        [TestMethod]
        public async Task EntregadoConReenvio_OtraPasadaSeHaAdelantado_NoAvisaDosVeces()
        {
            EnviosAgencia viejo = EntregadoConReenvio(Constantes.Agencias.ESTADO_EN_CURSO);
            datos.Repo.ForzarResultado = ResultadoAnulacionReenvio.SinReenvioPendiente;

            Assert.AreEqual(ResultadoReenvioEntregado.SinReenvio, await Entrega().ResolverAsync(viejo));
            Assert.AreEqual(0, avisosEntrega.Count);
        }

        [TestMethod]
        public async Task EntregadoSinReenvio_SinDecisionOMarcadoParaAbonarOSinTabla_NoHaceNada()
        {
            EnviosAgencia viejo = Devuelto(estado: Constantes.Agencias.ESTADO_ENTREGADO);
            Assert.AreEqual(ResultadoReenvioEntregado.SinReenvio, await Entrega().ResolverAsync(viejo));

            datos.Repo.Filas[251300] = new RegistroReenvioEnvio { EnvioOrigen = 251300, Decision = DecisionesReenvio.ABONAR };
            Assert.AreEqual(ResultadoReenvioEntregado.SinReenvio, await Entrega().ResolverAsync(viejo));

            EntregadoConReenvio(Constantes.Agencias.ESTADO_EN_CURSO);
            datos.Repo.Tabla = false;
            Assert.AreEqual(ResultadoReenvioEntregado.SinReenvio, await Entrega().ResolverAsync(viejo));

            Assert.AreEqual(0, datos.Repo.Anulaciones);
            Assert.AreEqual(0, avisosEntrega.Count);
        }

        [TestMethod]
        public async Task EntregadoConReenvio_SiFallaElAviso_NoSeCae()
        {
            EnviosAgencia viejo = EntregadoConReenvio(Constantes.Agencias.ESTADO_EN_CURSO);
            var entrega = new ReenvioEntregadoPorLaAgencia(datos, (o, f, n, a) => throw new Exception("SMTP caído"));

            Assert.AreEqual(ResultadoReenvioEntregado.ReenvioAnulado, await entrega.ResolverAsync(viejo));
        }

        [TestMethod]
        public void Devueltos_EntregadoPorLaAgencia_SeEnsenaConElReenvioAnulado()
        {
            var envios = new List<EnvioAgenciaListadoDTO>
            {
                new EnvioAgenciaListadoDTO { Numero = 1, Fecha = new DateTime(2026, 11, 3), Estado = Constantes.Agencias.ESTADO_ENTREGADO, Pedido = 11 }
            };
            var decisiones = new Dictionary<int, RegistroReenvioEnvio>
            {
                [1] = new RegistroReenvioEnvio { EnvioOrigen = 1, Decision = DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA, EnvioAnulado = 300000, Usuario = "Alfredo" }
            };

            EnvioDevueltoDTO d = ServicioReenviosEnvio.Combinar(envios, decisiones, new Dictionary<int, short>()).Single();

            Assert.AreEqual(DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA, d.Decision);
            Assert.IsNull(d.EnvioNuevo);
            Assert.AreEqual(300000, d.EnvioAnulado);
            Assert.IsFalse(d.EnvioNuevoConEtiqueta);
        }

        [TestMethod]
        public void ComponerCorreoReenvioEntregado_Anulado_DiceQueNoHayQueMandarNadaYLosPortes()
        {
            RegistroReenvioEnvio fila = new RegistroReenvioEnvio { EnvioOrigen = 251300, EnvioNuevo = 300000, IncluirPortes = true, Usuario = "Alfredo", Fecha = HOY };
            MailMessage mail = AvisadorIncidenciasEnvio.ComponerCorreoReenvioEntregado(Devuelto(estado: Constantes.Agencias.ESTADO_ENTREGADO), fila,
                new EnviosAgencia { Numero = 300000 }, true, Comerciales(), "CTT");

            Assert.AreEqual("La agencia entregó el envío 251300 del pedido 925137 - c/ 15191 - Peluquería Ejemplo: se anula el reenvío 300000", mail.Subject);
            Assert.AreEqual("santiago@nuevavision.es", mail.To.Single().Address);
            Assert.AreEqual(Constantes.Correos.LOGISTICA, mail.CC.Single().Address);
            StringAssert.Contains(mail.Body, "se ha anulado");
            StringAssert.Contains(mail.Body, "portes");
        }

        [TestMethod]
        public void ComponerCorreoReenvioEntregado_YaTramitado_AvisaDeDosEnviosVivos()
        {
            RegistroReenvioEnvio fila = new RegistroReenvioEnvio { EnvioOrigen = 251300, EnvioNuevo = 300000 };
            MailMessage mail = AvisadorIncidenciasEnvio.ComponerCorreoReenvioEntregado(Devuelto(estado: Constantes.Agencias.ESTADO_ENTREGADO), fila,
                new EnviosAgencia { Numero = 300000, CodigoBarras = "ETQ300000" }, false, Comerciales(), "CTT");

            StringAssert.Contains(mail.Subject, "ya está tramitado");
            StringAssert.Contains(mail.Body, "hay dos envíos vivos del mismo pedido");
            StringAssert.Contains(mail.Body, "ETQ300000");
            Assert.IsFalse(mail.Body.Contains("portes"));
            Assert.AreEqual(Constantes.Correos.LOGISTICA, mail.CC.Single().Address);
        }

        // ===== Los falsos =====

        private sealed class RepoFalso : IRepositorioReenviosEnvio
        {
            public bool Tabla { get; set; } = true;
            public Dictionary<int, RegistroReenvioEnvio> Filas { get; } = new Dictionary<int, RegistroReenvioEnvio>();
            public Action AlInsertar { get; set; }

            public bool TablaDisponible() => Tabla;

            public Task<RegistroReenvioEnvio> LeerAsync(int envioOrigen)
                => Task.FromResult(Filas.TryGetValue(envioOrigen, out RegistroReenvioEnvio f) ? f : null);

            public Task<Dictionary<int, RegistroReenvioEnvio>> LeerAsync(IEnumerable<int> enviosOrigen)
                => Task.FromResult(enviosOrigen.Where(Filas.ContainsKey).Distinct().ToDictionary(n => n, n => Filas[n]));

            /// <summary>Los envíos «de la BD» (los de DatosFalsos), para que la anulación borre el nuevo.</summary>
            public Dictionary<int, EnviosAgencia> Envios { get; set; }
            public ResultadoAnulacionReenvio? ForzarResultado { get; set; }
            public int Anulaciones { get; private set; }
            public string HistoriaAnulacion { get; private set; }
            public string UsuarioAnulacion { get; private set; }

            public Task<List<int>> LeerOrigenesConDecisionAsync(string decision)
                => Task.FromResult(Filas.Values.Where(f => f.Decision == decision).Select(f => f.EnvioOrigen).ToList());

            public Task<ResultadoAnulacionReenvio> AnularPorEntregaAsync(int envioOrigen, int envioNuevo, string usuario, string observaciones)
            {
                Anulaciones++;
                if (ForzarResultado.HasValue)
                {
                    return Task.FromResult(ForzarResultado.Value);
                }
                if (!Filas.TryGetValue(envioOrigen, out RegistroReenvioEnvio f) || f.Decision != DecisionesReenvio.REENVIAR || f.EnvioNuevo != envioNuevo)
                {
                    return Task.FromResult(ResultadoAnulacionReenvio.SinReenvioPendiente);
                }
                if (!Envios.TryGetValue(envioNuevo, out EnviosAgencia nuevo) || nuevo.Estado >= Constantes.Agencias.ESTADO_TRAMITADO)
                {
                    return Task.FromResult(ResultadoAnulacionReenvio.YaTramitado);
                }
                f.Decision = DecisionesReenvio.ENTREGADO_POR_LA_AGENCIA;
                f.EnvioAnulado = envioNuevo;
                f.EnvioNuevo = null;
                Envios.Remove(envioNuevo);
                HistoriaAnulacion = observaciones;
                UsuarioAnulacion = usuario;
                return Task.FromResult(ResultadoAnulacionReenvio.Anulado);
            }

            public Task<bool> InsertarAsync(RegistroReenvioEnvio registro)
            {
                AlInsertar?.Invoke();
                if (Filas.ContainsKey(registro.EnvioOrigen))
                {
                    return Task.FromResult(false);
                }
                registro.Fecha = HOY;
                Filas[registro.EnvioOrigen] = registro;
                return Task.FromResult(true);
            }
        }

        private sealed class DatosFalsos : IDatosReenviosEnvio
        {
            private int siguiente = 300000;
            public RepoFalso Repo { get; } = new RepoFalso();
            public IRepositorioReenviosEnvio Reenvios => Repo;
            public Dictionary<int, EnviosAgencia> Envios { get; } = new Dictionary<int, EnviosAgencia>();

            public DatosFalsos()
            {
                Repo.Envios = Envios;
            }
            public List<EnviosAgencia> Anadidos { get; } = new List<EnviosAgencia>();
            public List<EnviosAgencia> PortesAnadidos { get; } = new List<EnviosAgencia>();
            public AgenciaTransporte Agencia { get; set; }
            public decimal? ReembolsoQuitado { get; set; }
            public List<EnvioAgenciaListadoDTO> Listado { get; set; } = new List<EnvioAgenciaListadoDTO>();
            public Dictionary<int, short> Estados { get; } = new Dictionary<int, short>();
            public DateTime? DesdeLeido { get; private set; }
            public bool TransaccionDeshecha { get; private set; }

            public Task<EnviosAgencia> LeerEnvioAsync(int numero)
                => Task.FromResult(Envios.TryGetValue(numero, out EnviosAgencia e) ? e : null);

            public Task<AgenciaTransporte> LeerAgenciaAsync(int agencia) => Task.FromResult(Agencia);

            public Task<decimal?> ReembolsoQuitadoPorDevueltoAsync(int envio) => Task.FromResult(ReembolsoQuitado);

            public Task<List<EnvioAgenciaListadoDTO>> LeerDevueltosEIncidentadosAsync(string empresa, DateTime desde)
            {
                DesdeLeido = desde;
                return Task.FromResult(Listado);
            }

            public Task<Dictionary<int, short>> LeerEstadosAsync(IEnumerable<int> envios)
                => Task.FromResult(envios.Where(Estados.ContainsKey).ToDictionary(n => n, n => Estados[n]));

            public void Anadir(EnviosAgencia envio) => Anadidos.Add(envio);

            public Task GuardarCambios()
            {
                foreach (EnviosAgencia e in Anadidos.Where(e => e.Numero == 0))
                {
                    e.Numero = siguiente++;
                }
                return Task.CompletedTask;
            }

            public async Task<T> EnTransaccion<T>(Func<Task<T>> cuerpo)
            {
                int antes = Anadidos.Count;
                int portesAntes = PortesAnadidos.Count;
                try
                {
                    return await cuerpo();
                }
                catch
                {
                    Anadidos.RemoveRange(antes, Anadidos.Count - antes);
                    PortesAnadidos.RemoveRange(portesAntes, PortesAnadidos.Count - portesAntes);
                    TransaccionDeshecha = true;
                    throw;
                }
            }

            public decimal AnadirLineaPortes(EnviosAgencia envio, string usuario)
            {
                PortesAnadidos.Add(envio);
                return 6.5M;
            }
        }
    }
}
