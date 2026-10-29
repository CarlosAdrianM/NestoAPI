using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Repositorio en memoria de ConsultasReembolsoPedido, del pedido, de sus enlaces y de sus envíos (NestoAPI#734).</summary>
    internal class RepositorioConsultasReembolsoEnMemoria : IRepositorioConsultasReembolso
    {
        public bool Existe { get; set; } = true;
        public List<ConsultaReembolsoDTO> Filas { get; } = new List<ConsultaReembolsoDTO>();
        public Dictionary<int, DatosPagoPedidoConsulta> Pedidos { get; } = new Dictionary<int, DatosPagoPedidoConsulta>();
        public List<EnlacePagoPedido> Enlaces { get; } = new List<EnlacePagoPedido>();
        public List<EnviosAgencia> Envios { get; } = new List<EnviosAgencia>();
        public Dictionary<string, FormaPagoConsulta> Formas { get; } = new Dictionary<string, FormaPagoConsulta>
        {
            ["RCB"] = new FormaPagoConsulta { Numero = "RCB", Descripcion = "Recibo bancario", CCCObligatorio = true },
            ["TRN"] = new FormaPagoConsulta { Numero = "TRN", Descripcion = "Transferencia" },
            ["TAR"] = new FormaPagoConsulta { Numero = "TAR", Descripcion = "Tarjeta" },
            ["EFC"] = new FormaPagoConsulta { Numero = "EFC", Descripcion = "Efectivo" }
        };
        public HashSet<string> Plazos { get; } = new HashSet<string> { "CONTADO", "PRE", "1/30" };
        public decimal Reembolso { get; set; } = 258.56m;
        public decimal Prepagos { get; set; }
        public bool FallarCambioFormaPago { get; set; }
        public List<(int pedido, string forma, string plazos, string ccc, string usuario, string anterior, string nuevo)> CambiosFormaPago { get; } =
            new List<(int, string, string, string, string, string, string)>();
        public List<int> Anulados { get; } = new List<int>();
        private int siguienteId = 1;

        public DatosPagoPedidoConsulta Pedido(int numero, string forma = "EFC", string plazos = "CONTADO", string cccCliente = "1")
        {
            var pedido = new DatosPagoPedidoConsulta
            {
                Empresa = "1", Pedido = numero, Cliente = "40026", Contacto = "0", NombreCliente = "CLIENTA", FormaPago = forma, PlazosPago = plazos,
                PeriodoFacturacion = "NRM", CCCCliente = cccCliente
            };
            Pedidos[numero] = pedido;
            return pedido;
        }

        public EnlacePagoPedido Enlace(int id, decimal importe, DateTime creado, string estado = Constantes.EstadosPagoTPV.PENDIENTE)
        {
            var enlace = new EnlacePagoPedido { Id = id, Importe = importe, Estado = estado, FechaCreacion = creado, Descripcion = "Pago pedido 929300 de Nueva Visión" };
            Enlaces.Add(enlace);
            return enlace;
        }

        private static ConsultaReembolsoDTO Copia(ConsultaReembolsoDTO c) => c == null ? null : (ConsultaReembolsoDTO)c.GetType().GetMethod("MemberwiseClone",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(c, null);

        public bool TablaExiste() => Existe;
        public Task<ConsultaReembolsoDTO> LeerAbierta(string empresa, int pedido)
            => Task.FromResult(Copia(Filas.Where(f => f.Pedido == pedido && f.Respondida == null).OrderByDescending(f => f.Id).FirstOrDefault()));
        public Task<ConsultaReembolsoDTO> LeerUltima(string empresa, int pedido)
            => Task.FromResult(Existe ? Copia(Filas.Where(f => f.Pedido == pedido).OrderByDescending(f => f.Id).FirstOrDefault()) : null);
        public Task<List<ConsultaReembolsoListadoDTO>> LeerListado(string empresa, bool soloAbiertas)
            => Task.FromResult(Filas.Where(f => !soloAbiertas || f.Respondida == null)
                .Select(f => new ConsultaReembolsoListadoDTO { Id = f.Id, Pedido = f.Pedido, Respondida = f.Respondida, Cliente = "40026" }).ToList());
        public Task<int> Insertar(ConsultaReembolsoDTO consulta)
        {
            var fila = Copia(consulta);
            fila.Id = siguienteId++;
            Filas.Add(fila);
            return Task.FromResult(fila.Id);
        }
        public Task<bool> Responder(ConsultaReembolsoDTO consulta)
        {
            int i = Filas.FindIndex(f => f.Id == consulta.Id && f.Respondida == null);
            if (i < 0)
            {
                return Task.FromResult(false);
            }
            Filas[i] = Copia(consulta);
            return Task.FromResult(true);
        }
        public Task Reabrir(int id)
        {
            ConsultaReembolsoDTO f = Filas.Single(x => x.Id == id);
            f.Respondida = null;
            f.UsuarioRespuesta = null;
            f.ConReembolso = null;
            f.FormaPago = null;
            f.PlazosPago = null;
            return Task.CompletedTask;
        }
        public Task ApuntarEnlacesAnulados(int id, string anulados)
        {
            Filas.Single(x => x.Id == id).EnlacesAnulados = anulados;
            return Task.CompletedTask;
        }
        public Task<List<EnlacePagoPedido>> LeerEnlaces(string empresa, int pedido) => Task.FromResult(Enlaces.ToList());
        public Task<DatosPagoPedidoConsulta> LeerDatosPago(string empresa, int pedido)
            => Task.FromResult(Pedidos.TryGetValue(pedido, out DatosPagoPedidoConsulta p) ? p : null);
        public Task<FormaPagoConsulta> LeerFormaPago(string empresa, string formaPago)
            => Task.FromResult(Formas.TryGetValue(formaPago ?? string.Empty, out FormaPagoConsulta f) ? f : null);
        public Task<bool> ExistenPlazosPago(string empresa, string plazosPago) => Task.FromResult(Plazos.Contains(plazosPago ?? string.Empty));
        public decimal ImporteReembolso(string empresa, int pedido) => Reembolso;
        public decimal PrepagosPendientes(string empresa, int pedido) => Prepagos;
        public Task CambiarFormaPago(string empresa, int pedido, string formaPago, string plazosPago, string ccc, string usuario, string anterior, string nuevo)
        {
            if (FallarCambioFormaPago)
            {
                throw new InvalidOperationException("caída");
            }
            CambiosFormaPago.Add((pedido, formaPago, plazosPago, ccc, usuario, anterior, nuevo));
            DatosPagoPedidoConsulta p = Pedidos[pedido];
            p.FormaPago = formaPago;
            p.PlazosPago = plazosPago;
            p.CCC = ccc;
            return Task.CompletedTask;
        }
        public Task<List<int>> AnularEnlaces(string empresa, IEnumerable<int> ids)
        {
            var anulados = new List<int>();
            foreach (EnlacePagoPedido e in Enlaces.Where(x => ids.Contains(x.Id) && x.Estado == Constantes.EstadosPagoTPV.PENDIENTE))
            {
                e.Estado = Constantes.EstadosPagoTPV.CANCELADO;
                anulados.Add(e.Id);
            }
            Anulados.AddRange(anulados);
            return Task.FromResult(anulados);
        }
        public Task<List<EnviosAgencia>> LeerEnvios(string empresa, int pedido) => Task.FromResult(Envios.ToList());
    }

    internal class AvisadorConsultaReembolsoEnMemoria : IAvisadorConsultaReembolso
    {
        public int MiembrosAdministracion { get; set; } = 2;
        public List<NotificacionPushDTO> AAdministracion { get; } = new List<NotificacionPushDTO>();
        public List<(string usuario, NotificacionPushDTO notificacion)> AUsuarios { get; } = new List<(string, NotificacionPushDTO)>();

        public Task<int> AvisarAdministracion(NotificacionPushDTO notificacion)
        {
            AAdministracion.Add(notificacion);
            return Task.FromResult(MiembrosAdministracion);
        }

        public Task AvisarA(string usuario, NotificacionPushDTO notificacion)
        {
            AUsuarios.Add((usuario, notificacion));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// NestoAPI#734 (Carlos, 29/10/26): el almacén retiene un pedido contra reembolso con un enlace de pago pendiente y consulta a
    /// Administración, que decide con o sin reembolso.
    /// </summary>
    [TestClass]
    public class ConsultaReembolsoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 929300;
        private const string ALMACEN = "NUEVAVISION\\Alfredo";
        private const string ADMINISTRACION = "NUEVAVISION\\Paloma";
        private static readonly DateTime Ahora = new DateTime(2026, 10, 29, 10, 0, 0);

        private RepositorioConsultasReembolsoEnMemoria repo;
        private RepositorioPedidosRetenidosEnMemoria retenidos;
        private AvisadorConsultaReembolsoEnMemoria avisador;
        private List<Exception> registrados;
        private ServicioConsultasReembolso servicio;

        [TestInitialize]
        public void Preparar()
        {
            repo = new RepositorioConsultasReembolsoEnMemoria();
            repo.Pedido(PEDIDO);
            repo.Enlace(900, 252.94m, Ahora.AddDays(-3));
            retenidos = new RepositorioPedidosRetenidosEnMemoria();
            _ = retenidos.Cabecera(PEDIDO, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO);
            avisador = new AvisadorConsultaReembolsoEnMemoria();
            registrados = new List<Exception>();
            servicio = new ServicioConsultasReembolso(repo, retenidos, avisador, reloj: () => Ahora, registrar: registrados.Add);
        }

        private static string N(string texto) => texto?.Replace(' ', ' ').Replace(' ', ' ');

        private Task<ResultadoConsultaReembolso> Consultar() => servicio.Consultar(EMPRESA, PEDIDO, new PedirConsultaReembolsoRequest { Comentario = "Lo dice el F8" }, ALMACEN);

        private Task<ResultadoConsultaReembolso> Responder(ResponderConsultaReembolsoRequest peticion) => servicio.Responder(EMPRESA, PEDIDO, peticion, ADMINISTRACION);

        // ================================================================ consultar

        [TestMethod]
        public async Task Consultar_RetieneElPedidoGuardaLaConsultaYAvisaAAdministracion()
        {
            ResultadoConsultaReembolso r = await Consultar();

            Assert.IsNull(r.Error, r.Error);
            Assert.IsFalse(r.Respuesta.YaExistia);
            ConsultaReembolsoDTO fila = repo.Filas.Single();
            Assert.AreEqual(PEDIDO, fila.Pedido);
            Assert.AreEqual(ALMACEN, fila.Usuario);
            Assert.AreEqual(252.94m, fila.ImporteEnlaces);
            Assert.AreEqual(1, fila.NumeroEnlaces);
            Assert.AreEqual("Lo dice el F8", fila.Comentario);
            Assert.IsTrue(fila.Abierta);
            // Retenido como #698 (modo 5) con el motivo de la consulta
            RetencionPedidoDTO retencion = retenidos.Filas.Single();
            Assert.AreEqual(retencion.Id, fila.RetencionId);
            StringAssert.Contains(N(retencion.Motivo), "Enlace de pago pendiente de 252,94 €: consultando a Administración");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.RETENIDO, retenidos.Cabeceras[PEDIDO].ModoServicio);
            // Campana de Administración
            NotificacionPushDTO aviso = avisador.AAdministracion.Single();
            Assert.AreEqual(TextosConsultaReembolso.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual("consulta", aviso.Datos["accion"]);
            Assert.AreEqual(PEDIDO.ToString(), aviso.Datos["pedido"]);
            StringAssert.Contains(aviso.Titulo, "929300");
            StringAssert.Contains(aviso.Cuerpo, "Alfredo ha retenido el pedido 929300 del cliente 40026 (CLIENTA)");
            StringAssert.Contains(aviso.Cuerpo, "Pago pedido 929300 de Nueva Visión");
            Assert.AreEqual(0, r.Respuesta.Avisos.Count);
        }

        [TestMethod]
        public async Task Consultar_DosVeces_NoDuplicaNiVuelveARetenerNiAAvisar()
        {
            _ = await Consultar();
            ResultadoConsultaReembolso segunda = await Consultar();

            Assert.IsNull(segunda.Error, segunda.Error);
            Assert.IsTrue(segunda.Respuesta.YaExistia);
            Assert.AreEqual(1, repo.Filas.Count);
            Assert.AreEqual(1, retenidos.Filas.Count);
            Assert.AreEqual(1, avisador.AAdministracion.Count);
            Assert.AreEqual(repo.Filas.Single().Id, segunda.Respuesta.Consulta.Id);
        }

        [TestMethod]
        public async Task Consultar_SinEnlacesPendientesVivos_NoRetieneYLoDice()
        {
            repo.Enlaces.Clear();
            repo.Enlace(901, 100m, Ahora.AddDays(-40)); // abandonado (más de 30 días)

            ResultadoConsultaReembolso r = await Consultar();

            Assert.AreEqual(TextosConsultaReembolso.ERROR_SIN_ENLACES, r.Error);
            Assert.AreEqual(0, repo.Filas.Count);
            Assert.AreEqual(0, retenidos.Filas.Count);
        }

        [TestMethod]
        public async Task Consultar_SinTabla_ErrorClaro()
        {
            repo.Existe = false;

            ResultadoConsultaReembolso r = await Consultar();

            Assert.AreEqual(TextosConsultaReembolso.ERROR_SIN_TABLA, r.Error);
            Assert.AreEqual(0, retenidos.Filas.Count);
        }

        [TestMethod]
        public async Task Consultar_PedidoQueNoExiste_NoEncontrado()
        {
            ResultadoConsultaReembolso r = await servicio.Consultar(EMPRESA, 1, null, ALMACEN);

            Assert.IsTrue(r.NoEncontrado);
        }

        [TestMethod]
        public async Task Consultar_YaRetenidoPorOtraCosa_NoTocaEsaRetencion()
        {
            _ = await retenidos.Insertar(new RetencionPedidoDTO { Pedido = PEDIDO, Motivo = "El cliente lo quiere para noviembre", Usuario = "NUEVAVISION\\Laura", Fecha = Ahora.AddDays(-1) });

            ResultadoConsultaReembolso r = await Consultar();

            Assert.IsNull(r.Error, r.Error);
            Assert.IsNull(repo.Filas.Single().RetencionId);
            Assert.AreEqual(1, retenidos.Filas.Count);
        }

        [TestMethod]
        public async Task Consultar_SinNadieEnAdministracion_LoDiceYLaConsultaQueda()
        {
            avisador.MiembrosAdministracion = 0;

            ResultadoConsultaReembolso r = await Consultar();

            Assert.IsNull(r.Error, r.Error);
            Assert.AreEqual(1, repo.Filas.Count);
            CollectionAssert.Contains(r.Respuesta.Avisos, TextosConsultaReembolso.AVISO_SIN_ADMINISTRACION);
            Assert.AreEqual(1, registrados.Count);
        }

        // ================================================================ responder

        [TestMethod]
        public async Task Responder_ConReembolso_LiberaAvisaYNoAnulaLosEnlaces()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true });

            Assert.IsNull(r.Error, r.Error);
            ConsultaReembolsoDTO fila = repo.Filas.Single();
            Assert.AreEqual(true, fila.ConReembolso);
            Assert.AreEqual(ADMINISTRACION, fila.UsuarioRespuesta);
            Assert.AreEqual(Ahora, fila.Respondida);
            Assert.AreEqual(false, fila.AnularEnlaces);
            Assert.AreEqual(0, repo.Anulados.Count, "los enlaces nunca se anulan solos: pueden ser de otro pedido");
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repo.Enlaces.Single().Estado);
            Assert.AreEqual(0, repo.CambiosFormaPago.Count);
            // Liberado: vuelve a su modo
            Assert.IsTrue(r.Respuesta.Liberado);
            Assert.IsNotNull(retenidos.Filas.Single().Liberado);
            StringAssert.Contains(retenidos.Filas.Single().MotivoLiberacion, "Administración (Paloma): sale con reembolso");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, retenidos.Cabeceras[PEDIDO].ModoServicio);
            // Aviso a quien preguntó
            (string usuario, NotificacionPushDTO notificacion) = avisador.AUsuarios.Single();
            Assert.AreEqual(ALMACEN, usuario);
            Assert.AreEqual("respuesta", notificacion.Datos["accion"]);
            Assert.AreEqual("true", notificacion.Datos["conReembolso"]);
            StringAssert.Contains(notificacion.Cuerpo, "Administración (Paloma) dice: el pedido 929300 del cliente 40026 (CLIENTA) sale con reembolso.");
            StringAssert.Contains(notificacion.Cuerpo, "puede hacer el envío");
        }

        [TestMethod]
        public async Task Responder_ConReembolsoYAnularEnlaces_AnulaLosPendientes()
        {
            repo.Enlace(902, 50m, Ahora.AddDays(-1));
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true, AnularEnlaces = true });

            Assert.IsNull(r.Error, r.Error);
            CollectionAssert.AreEquivalent(new[] { 900, 902 }, r.Respuesta.EnlacesAnulados);
            Assert.IsTrue(repo.Enlaces.All(e => e.Estado == Constantes.EstadosPagoTPV.CANCELADO));
            CollectionAssert.AreEquivalent(new[] { "900", "902" }, repo.Filas.Single().EnlacesAnulados.Split(','));
            StringAssert.Contains(avisador.AUsuarios.Single().notificacion.Cuerpo, "Se han anulado los 2 enlaces de pago pendientes.");
        }

        [TestMethod]
        public async Task Responder_AnularSoloAlgunos_AnulaEsosYRechazaLosAjenos()
        {
            repo.Enlace(902, 50m, Ahora.AddDays(-1));
            _ = await Consultar();

            ResultadoConsultaReembolso ajeno = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true, AnularEnlaces = true, EnlacesAAnular = new List<int> { 777 } });
            Assert.IsNotNull(ajeno.Error);
            Assert.IsTrue(repo.Filas.Single().Abierta, "una respuesta que no vale no toca nada");

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true, AnularEnlaces = true, EnlacesAAnular = new List<int> { 902 } });

            CollectionAssert.AreEqual(new[] { 902 }, r.Respuesta.EnlacesAnulados);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repo.Enlaces.Single(e => e.Id == 900).Estado);
        }

        [TestMethod]
        public async Task Responder_SinReembolsoConRecibo_CambiaFormaPagoYPlazosConElCCCDelCliente()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "rcb", PlazosPago = "1/30", Comentario = "Lo pactó con Paloma" });

            Assert.IsNull(r.Error, r.Error);
            var cambio = repo.CambiosFormaPago.Single();
            Assert.AreEqual("RCB", cambio.forma);
            Assert.AreEqual("1/30", cambio.plazos);
            Assert.AreEqual("1", cambio.ccc, "con recibo, el CCC de la ficha: si no, seguiría siendo contra reembolso");
            Assert.AreEqual(ADMINISTRACION, cambio.usuario);
            StringAssert.Contains(cambio.anterior, "FormaPago=EFC PlazosPago=CONTADO");
            StringAssert.Contains(cambio.nuevo, "FormaPago=RCB PlazosPago=1/30 CCC=1");
            ConsultaReembolsoDTO fila = repo.Filas.Single();
            Assert.AreEqual(false, fila.ConReembolso);
            Assert.AreEqual("EFC", fila.FormaPagoAnterior);
            Assert.AreEqual("CONTADO", fila.PlazosPagoAnterior);
            Assert.AreEqual("RCB", fila.FormaPago);
            Assert.AreEqual("1/30", fila.PlazosPago);
            Assert.IsTrue(r.Respuesta.Liberado);
            Assert.IsFalse(GestorPortesEsContraReembolso(repo.Pedidos[PEDIDO]), "el pedido ya no va contra reembolso");
            NotificacionPushDTO aviso = avisador.AUsuarios.Single().notificacion;
            Assert.AreEqual("false", aviso.Datos["conReembolso"]);
            StringAssert.Contains(aviso.Cuerpo, "sale sin reembolso (RCB, 1/30)");
            StringAssert.Contains(aviso.Cuerpo, "«Lo pactó con Paloma»");
        }

        private static bool GestorPortesEsContraReembolso(DatosPagoPedidoConsulta p)
            => NestoAPI.Infraestructure.PedidosVenta.GestorPortes.EsContraReembolso(p.FormaPago, p.PlazosPago, p.CCC, p.PeriodoFacturacion, p.NotaEntrega);

        [TestMethod]
        public async Task Responder_SinReembolsoATarjetaSinPagar_SeRechazaYNoSeTocaNada()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TAR", PlazosPago = "PRE" });

            StringAssert.Contains(r.Error, "no está pagado");
            Assert.AreEqual(0, repo.CambiosFormaPago.Count);
            Assert.IsTrue(repo.Filas.Single().Abierta);
            Assert.IsNull(retenidos.Filas.Single().Liberado, "sigue retenido");
            Assert.AreEqual(0, avisador.AUsuarios.Count);
        }

        [TestMethod]
        public async Task Responder_SinReembolsoConPlazosPrepagoSinPagar_SeRechaza()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TRN", PlazosPago = "PRE" });

            StringAssert.Contains(r.Error, "no está pagado");
        }

        [TestMethod]
        public async Task Responder_SinReembolsoATarjetaYaPagado_SePermite()
        {
            repo.Prepagos = 258.56m;
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TAR", PlazosPago = "PRE" });

            Assert.IsNull(r.Error, r.Error);
            Assert.AreEqual("TAR", repo.CambiosFormaPago.Single().forma);
            Assert.IsNull(repo.CambiosFormaPago.Single().ccc);
        }

        [TestMethod]
        public async Task Responder_SinReembolsoSinFormaPago_PideLaFormaPago()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false });

            Assert.AreEqual(TextosConsultaReembolso.ERROR_SIN_FORMA_PAGO, r.Error);
        }

        [TestMethod]
        public async Task Responder_SinReembolsoConUnaFormaQueSigueSiendoReembolso_SeRechaza()
        {
            repo.Pedido(PEDIDO, forma: "TRN"); // da igual lo que tuviera
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "EFC", PlazosPago = "CONTADO" });

            StringAssert.Contains(r.Error, "seguiría saliendo contra reembolso");
        }

        [TestMethod]
        public async Task Responder_SinReembolsoConReciboYClienteSinCuenta_SeRechaza()
        {
            repo.Pedido(PEDIDO, cccCliente: null);
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "RCB" });

            StringAssert.Contains(r.Error, "no tiene cuenta bancaria");
        }

        [TestMethod]
        public async Task Responder_SinReembolsoConFormaQueNoExiste_SeRechaza()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "XYZ" });

            StringAssert.Contains(r.Error, "no existe");
        }

        [TestMethod]
        public async Task Responder_SinReembolsoYEnvioYaEnReparto_AvisaDeQueHayQueCambiarloEnAgencias()
        {
            repo.Envios.Add(new EnviosAgencia { Numero = 250001, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Reembolso = 258.56m });
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TRN" });

            Assert.IsNull(r.Error, r.Error);
            Assert.IsTrue(r.Respuesta.Avisos.Any(a => a.Contains("250001") && a.Contains("Agencias")), string.Join(" | ", r.Respuesta.Avisos));
        }

        [TestMethod]
        public async Task Responder_SiFallaElCambioDeFormaPago_LaConsultaSigueAbierta()
        {
            _ = await Consultar();
            repo.FallarCambioFormaPago = true;

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TRN" }));

            Assert.IsTrue(repo.Filas.Single().Abierta);
            Assert.IsNull(retenidos.Filas.Single().Liberado);
        }

        [TestMethod]
        public async Task Responder_SinDecision_LaPide()
        {
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest());

            Assert.AreEqual(TextosConsultaReembolso.ERROR_SIN_DECISION, r.Error);
        }

        [TestMethod]
        public async Task Responder_SinConsulta_Conflicto()
        {
            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true });

            Assert.IsTrue(r.Conflicto);
            Assert.AreEqual(TextosConsultaReembolso.ERROR_SIN_CONSULTA, r.Error);
        }

        [TestMethod]
        public async Task Responder_DosVeces_LaSegundaEsConflictoYDiceQueSeContesto()
        {
            _ = await Consultar();
            _ = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true });

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = false, FormaPago = "TRN" });

            Assert.IsTrue(r.Conflicto);
            StringAssert.Contains(r.Error, "sale con reembolso");
            Assert.AreEqual(0, repo.CambiosFormaPago.Count);
        }

        [TestMethod]
        public async Task Responder_RetenidoPorOtraCosa_NoLoLibera()
        {
            _ = await retenidos.Insertar(new RetencionPedidoDTO { Pedido = PEDIDO, Motivo = "Pendiente de pago", Usuario = "NUEVAVISION\\Laura", Fecha = Ahora.AddDays(-1) });
            _ = await Consultar();

            ResultadoConsultaReembolso r = await Responder(new ResponderConsultaReembolsoRequest { ConReembolso = true });

            Assert.IsFalse(r.Respuesta.Liberado);
            Assert.IsNull(retenidos.Filas.Single().Liberado);
            Assert.IsTrue(r.Respuesta.Avisos.Any(a => a.Contains("sigue retenido")));
            StringAssert.Contains(avisador.AUsuarios.Single().notificacion.Cuerpo, "sigue retenido por otro motivo");
        }

        [TestMethod]
        public async Task Listado_AbiertasConSusEnlacesPendientes()
        {
            _ = await Consultar();

            List<ConsultaReembolsoListadoDTO> lista = await servicio.Listado(EMPRESA, true);

            Assert.AreEqual(900, lista.Single().EnlacesPendientes.Single().Id);
        }

        // ================================================================ la propuesta de envío

        private static IDatosPropuestaEnvio DatosPropuesta(decimal reembolso, List<EnlacePagoPedido> enlaces, EnviosAgencia pendiente = null)
        {
            var datos = A.Fake<IDatosPropuestaEnvio>();
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(new PedidoParaAgenciaDTO
            {
                Empresa = "1  ",
                Numero = PEDIDO,
                Cliente = "40026     ",
                Contacto = "0  ",
                Vendedor = "JE ",
                ClienteFicha = new ClienteParaAgenciaDTO { Nombre = "CLIENTA", Direccion = "Calle Mayor, 1", CodPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID" }
            }));
            A.CallTo(() => datos.TieneAlgunaLineaConPicking(EMPRESA, PEDIDO)).Returns(Task.FromResult(true));
            A.CallTo(() => datos.LeerEnvioPendiente(A<string>._, A<int>._)).Returns(Task.FromResult(pendiente));
            A.CallTo(() => datos.LeerEnvioAmpliacion(A<string>._, A<string>._, A<string>._, A<int?>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.ImporteReembolso(EMPRESA, PEDIDO)).Returns(reembolso);
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Returns(0m);
            A.CallTo(() => datos.EnlacesDePago(EMPRESA, PEDIDO)).Returns(enlaces);
            A.CallTo(() => datos.BultosPacking(EMPRESA, PEDIDO)).Returns(Task.FromResult(0));
            A.CallTo(() => datos.FechaPicking(A<string>._)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 29)));
            A.CallTo(() => datos.LeerAgencias()).Returns(Task.FromResult(new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM" }
            }));
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(new ITarifaAgencia[] { new TarifaGLSBusinessParcel() });
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => datos.Comparador()).ReturnsLazily(() => new ComparadorAgencias(registro, fuel));
            return datos;
        }

        private static List<EnlacePagoPedido> EnlacePendiente(DateTime creado)
            => new List<EnlacePagoPedido> { new EnlacePagoPedido { Id = 900, Importe = 252.94m, Estado = Constantes.EstadosPagoTPV.PENDIENTE, FechaCreacion = creado } };

        private static Task<PropuestaEnvioDTO> Propuesta(IDatosPropuestaEnvio datos, ConsultaReembolsoDTO consulta)
            => new PropuestaEnvioService(datos, (e, p) => Task.FromResult(consulta)).Calcular(EMPRESA, PEDIDO, 1, 1m);

        [TestMethod]
        public async Task Propuesta_SinConsulta_AvisaDelEnlaceYOfreceConsultarAAdministracion()
        {
            PropuestaEnvioDTO p = await Propuesta(DatosPropuesta(258.56m, EnlacePendiente(DateTime.Now.AddDays(-3))), null);

            AvisoPropuestaEnvio aviso = p.AvisosDetalle.Single(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE);
            Assert.AreEqual(AccionesAvisoPropuestaEnvio.CONSULTAR_ADMINISTRACION, aviso.Accion);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.CONFIRMAR, aviso.Tipo, "los clientes que solo leen Texto/Tipo siguen igual");
            StringAssert.Contains(aviso.Texto, "¿seguro que sale contra reembolso?");
            Assert.IsNull(p.ConsultaReembolso);
        }

        [TestMethod]
        public async Task Propuesta_ConConsultaAbierta_AvisaDeQueSeEstaConsultandoYNoDelEnlace()
        {
            var consulta = new ConsultaReembolsoDTO { Id = 1, Pedido = PEDIDO, Usuario = ALMACEN, Fecha = DateTime.Now.AddHours(-1), ImporteEnlaces = 252.94m };

            PropuestaEnvioDTO p = await Propuesta(DatosPropuesta(258.56m, EnlacePendiente(DateTime.Now.AddDays(-3))), consulta);

            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
            AvisoPropuestaEnvio aviso = p.AvisosDetalle.Single(a => a.Codigo == CodigosAvisoPropuestaEnvio.CONSULTA_REEMBOLSO_ABIERTA);
            Assert.AreEqual(TiposAvisoPropuestaEnvio.BLOQUEANTE, aviso.Tipo);
            StringAssert.Contains(aviso.Texto, "Alfredo consultó a Administración");
            Assert.IsTrue(p.Avisos.Contains(aviso.Texto));
            Assert.AreSame(consulta, p.ConsultaReembolso);
        }

        [TestMethod]
        public async Task Propuesta_TrasDecidirConReembolso_NoVuelveAAvisar()
        {
            var consulta = new ConsultaReembolsoDTO { Id = 1, Pedido = PEDIDO, Fecha = DateTime.Now.AddHours(-2), Respondida = DateTime.Now.AddHours(-1), ConReembolso = true };

            PropuestaEnvioDTO p = await Propuesta(DatosPropuesta(258.56m, EnlacePendiente(DateTime.Now.AddDays(-3))), consulta);

            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE || a.Codigo == CodigosAvisoPropuestaEnvio.CONSULTA_REEMBOLSO_ABIERTA),
                string.Join(" | ", p.Avisos));
            Assert.AreEqual(258.56m, p.Reembolso, "con reembolso: el de siempre");
        }

        [TestMethod]
        public async Task Propuesta_TrasDecidirPeroConUnEnlaceNuevo_VuelveAAvisar()
        {
            var consulta = new ConsultaReembolsoDTO { Id = 1, Pedido = PEDIDO, Fecha = DateTime.Now.AddDays(-2), Respondida = DateTime.Now.AddDays(-1), ConReembolso = true };

            PropuestaEnvioDTO p = await Propuesta(DatosPropuesta(258.56m, EnlacePendiente(DateTime.Now.AddHours(-1))), consulta);

            Assert.IsTrue(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
        }

        [TestMethod]
        public async Task Propuesta_TrasDecidirSinReembolso_UnEnvioPendienteSaleSinReembolso()
        {
            var pendiente = new EnviosAgencia { Numero = 250002, Empresa = "1  ", Agencia = 1, Reembolso = 258.56m, CodPostal = "28001", Pais = 34, Fecha = DateTime.Today };
            var consulta = new ConsultaReembolsoDTO { Id = 1, Pedido = PEDIDO, Fecha = DateTime.Now.AddHours(-2), Respondida = DateTime.Now.AddHours(-1), ConReembolso = false, FormaPago = "TRN" };

            PropuestaEnvioDTO p = await Propuesta(DatosPropuesta(0m, EnlacePendiente(DateTime.Now.AddDays(-3)), pendiente), consulta);

            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO, p.Origen);
            Assert.AreEqual(0m, p.Reembolso);
            Assert.IsFalse(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
        }

        [TestMethod]
        public async Task Propuesta_SiFallaLaLecturaDeLaConsulta_SigueComoAntes()
        {
            var servicioPropuesta = new PropuestaEnvioService(DatosPropuesta(258.56m, EnlacePendiente(DateTime.Now.AddDays(-3))),
                (e, n) => throw new InvalidOperationException("sin tabla"));

            PropuestaEnvioDTO p = await servicioPropuesta.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsTrue(p.AvisosDetalle.Any(a => a.Codigo == CodigosAvisoPropuestaEnvio.ENLACE_PAGO_PENDIENTE));
        }

        [TestMethod]
        public void DecisionCubreLosEnlaces_SoloSiEstaContestadaYLosEnlacesSonDeAntes()
        {
            var enlaces = EnlacePendiente(new DateTime(2026, 10, 28));
            Assert.IsFalse(PropuestaEnvioService.DecisionCubreLosEnlaces(null, enlaces));
            Assert.IsFalse(PropuestaEnvioService.DecisionCubreLosEnlaces(new ConsultaReembolsoDTO(), enlaces), "abierta");
            Assert.IsTrue(PropuestaEnvioService.DecisionCubreLosEnlaces(new ConsultaReembolsoDTO { Respondida = new DateTime(2026, 10, 29) }, enlaces));
            Assert.IsFalse(PropuestaEnvioService.DecisionCubreLosEnlaces(new ConsultaReembolsoDTO { Respondida = new DateTime(2026, 10, 27) }, enlaces));
        }

        // ================================================================ enlace anulado

        [TestMethod]
        public void EnlaceAnulado_EsElEstadoCancelado()
        {
            Assert.IsTrue(EnlacesPagoPedido.EstaAnulado("Cancelado"));
            Assert.IsTrue(EnlacesPagoPedido.EstaAnulado(" cancelado "));
            Assert.IsFalse(EnlacesPagoPedido.EstaAnulado(Constantes.EstadosPagoTPV.PENDIENTE));
            Assert.IsFalse(EnlacesPagoPedido.EstaAnulado(null));
        }

        // ================================================================ avisador

        [TestMethod]
        public async Task Avisador_AAdministracionPorLaCampanaYAQuienPreguntoEnNestoYAriadna()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var avisadorReal = new AvisadorConsultaReembolso(push, () => new List<string> { "Paloma", "NUEVAVISION\\Laura", "paloma", "(grupo vacío)" });
            var notificacion = new NotificacionPushDTO { Titulo = "t" };

            int avisados = await avisadorReal.AvisarAdministracion(notificacion);
            await avisadorReal.AvisarA("NUEVAVISION\\Alfredo", notificacion);

            Assert.AreEqual(2, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Paloma", Constantes.Aplicaciones.NESTO, notificacion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO, notificacion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", Constantes.Aplicaciones.NESTO, notificacion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("Alfredo", Constantes.Aplicaciones.ARIADNA, notificacion)).MustHaveHappenedOnceExactly();
        }

        // ================================================================ controlador

        private static PedidosVentaConsultaReembolsoController Controlador(ServicioConsultasReembolso servicio, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Prueba") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new PedidosVentaConsultaReembolsoController
            {
                CrearServicio = () => servicio,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"))
            };
        }

        private static async Task<HttpStatusCode> Codigo(IHttpActionResult resultado)
            => (await resultado.ExecuteAsync(CancellationToken.None)).StatusCode;

        [TestMethod]
        public async Task Controlador_PreguntanAlmacenTiendasYDireccion_NoAdministracion()
        {
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.ALMACEN).PostConsultaReembolso(EMPRESA, PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.TIENDAS).PostConsultaReembolso(EMPRESA, PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.DIRECCION).PostConsultaReembolso(EMPRESA, PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.ADMINISTRACION).PostConsultaReembolso(EMPRESA, PEDIDO, null)));
            Assert.AreEqual(1, repo.Filas.Count);
        }

        [TestMethod]
        public async Task Controlador_RespondenAdministracionYDireccion_NoAlmacen()
        {
            _ = await Consultar();
            var conReembolso = new ResponderConsultaReembolsoRequest { ConReembolso = true };

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.ALMACEN).PostRespuesta(EMPRESA, PEDIDO, conReembolso)));
            Assert.IsTrue(repo.Filas.Single().Abierta);
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.ADMINISTRACION).PostRespuesta(EMPRESA, PEDIDO, conReembolso)));
            Assert.AreEqual(HttpStatusCode.Conflict, await Codigo(await Controlador(servicio, Constantes.GruposSeguridad.DIRECCION).PostRespuesta(EMPRESA, PEDIDO, conReembolso)));
        }

        [TestMethod]
        public async Task Controlador_ErroresConSuCodigo()
        {
            PedidosVentaConsultaReembolsoController almacen = Controlador(servicio, Constantes.GruposSeguridad.ALMACEN);
            Assert.AreEqual(HttpStatusCode.NotFound, await Codigo(await almacen.PostConsultaReembolso(EMPRESA, 1, null)));
            Assert.AreEqual(HttpStatusCode.NoContent, await Codigo(await almacen.GetConsultaReembolso(EMPRESA, PEDIDO)));
            repo.Enlaces.Clear();
            HttpResponseMessage sinEnlaces = await (await almacen.PostConsultaReembolso(EMPRESA, PEDIDO, null)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.BadRequest, sinEnlaces.StatusCode);
            StringAssert.Contains(await sinEnlaces.Content.ReadAsStringAsync(), "\"Message\"");
        }

        [TestMethod]
        public async Task Controlador_UnClienteDeLaTiendaNunca()
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "cliente@x.es"), new Claim("cliente", "40026"),
                new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION) };
            var controlador = new PedidosVentaConsultaReembolsoController
            {
                CrearServicio = () => servicio,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"))
            };

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.PostConsultaReembolso(EMPRESA, PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetConsultasReembolso()));
        }
    }
}
