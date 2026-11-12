using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>NestoAPI#859: recogida imprevista del reparto propio («Me dan algo que no esperaba»).</summary>
    [TestClass]
    public class RecogidasImprevistasTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 12, 11, 0, 0);
        private static readonly Guid CLAVE = new Guid("6f1c1a2e-1111-4a2b-9c3d-123456789abc");

        private class RepositorioFalso : IRepositorioRecogidasImprevistas
        {
            public bool Script { get; set; } = true;
            public List<EnviosAgencia> Envios { get; } = new List<EnviosAgencia>();
            public Dictionary<string, FichaNotaCobro> Fichas { get; } = new Dictionary<string, FichaNotaCobro>
            {
                ["15191/0"] = new FichaNotaCobro { Nombre = "CENTRO DE ESTÉTICA LUNA", Direccion = "C/ Mayor 1", Poblacion = "Madrid", Vendedor = "JE", Ruta = "AT", Iva = "G21" }
            };
            public List<RecogidaImprevistaGuardada> Guardadas { get; } = new List<RecogidaImprevistaGuardada>();
            public List<(int Envio, string Texto)> Historia { get; } = new List<(int, string)>();
            public List<MovimientoBultoReparto> Movimientos { get; } = new List<MovimientoBultoReparto>();
            public int Creadas { get; private set; }
            public bool? AvisoUrgenteGuardado { get; private set; }

            public bool ScriptAplicado() => Script;
            public Task<EnviosAgencia> LeerEnvio(int id) => Task.FromResult(Envios.FirstOrDefault(e => e.Numero == id));
            public Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto) =>
                Task.FromResult(Fichas.TryGetValue($"{cliente}/{contacto}", out FichaNotaCobro f) ? f : null);
            public Task<List<EnviosAgencia>> LeerRetornosCandidatos(string empresa, string cliente, string contacto, DateTime desde) =>
                Task.FromResult(Envios.Where(e => e.Cliente == cliente && e.Contacto == contacto && e.Fecha >= desde).ToList());
            public Task<Dictionary<int, string>> NombresAgencias(IReadOnlyCollection<int> agencias) =>
                Task.FromResult(new Dictionary<int, string> { [8] = "CTT", [9] = "GLS", [14] = "Reparto propio" });
            public Task ApuntarHistoria(int envio, string observaciones, string usuario)
            {
                Historia.Add((envio, observaciones));
                return Task.CompletedTask;
            }
            public Task<RecogidaImprevistaGuardada> LeerPorClave(Guid clave) => Task.FromResult(Guardadas.FirstOrDefault());
            public Task<RecogidaImprevistaGuardada> Crear(FilaRecogidaSolicitada fila, Guid clave, short bultos, string texto, IReadOnlyList<string> lineas,
                FichaNotaCobro ficha, string usuario, string comentario, bool avisoUrgente, Func<int, IReadOnlyList<MovimientoBultoReparto>> movimientos)
            {
                Creadas++;
                AvisoUrgenteGuardado = avisoUrgente;
                Movimientos.AddRange(movimientos(930500));
                var g = new RecogidaImprevistaGuardada
                {
                    Id = 77, Empresa = fila.Empresa, Cliente = fila.Cliente, Contacto = fila.Contacto, Bultos = bultos, PedidoNota = 930500, AvisoUrgente = avisoUrgente
                };
                Guardadas.Add(g);
                return Task.FromResult(g);
            }
            public Task GuardarFoto(int id, string ruta) => Task.CompletedTask;
        }

        private class RefrescoFalso : IRefrescoSeguimientoEnvio
        {
            public bool Contesta { get; set; } = true;
            public short? NuevoEstado { get; set; }
            public Task<bool> Refrescar(EnviosAgencia envio)
            {
                if (Contesta && NuevoEstado.HasValue)
                {
                    envio.Estado = NuevoEstado.Value;
                }
                return Task.FromResult(Contesta);
            }
        }

        private class AvisosFalsos : IAvisosRecogidaImprevista
        {
            public List<(RecogidaImprevistaDTO Recogida, string Repartidor, int Retornos)> Avisos { get; } = new List<(RecogidaImprevistaDTO, string, int)>();
            public void Avisar(RecogidaImprevistaDTO recogida, string repartidor, IReadOnlyList<RetornoVivoDTO> retornosAceptados) =>
                Avisos.Add((recogida, repartidor, retornosAceptados?.Count ?? 0));
        }

        private RepositorioFalso repositorio;
        private RefrescoFalso refresco;
        private AvisosFalsos avisos;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioFalso();
            refresco = new RefrescoFalso();
            avisos = new AvisosFalsos();
            // La parada que lleva el repartidor (agencia 14)
            repositorio.Envios.Add(new EnviosAgencia { Numero = 251300, Empresa = "1  ", Agencia = 14, Cliente = "15191     ", Contacto = "0  ", Estado = 1, Fecha = AHORA.Date });
        }

        private ServicioRecogidasImprevistas Servicio() => new ServicioRecogidasImprevistas(repositorio, refresco, avisos, null, () => AHORA);

        private void ConRetornoVivo(short estado = 1, int agencia = 8, short retorno = 1)
            => repositorio.Envios.Add(new EnviosAgencia
            {
                Numero = 250900, Empresa = "1", Agencia = agencia, Cliente = "15191", Contacto = "0", Estado = estado, Retorno = retorno,
                Fecha = AHORA.Date.AddDays(-3), Pedido = 930100, Bultos = 2, CodigoBarras = "CTT123"
            });

        private static CrearRecogidaImprevistaDTO Peticion(bool acepta = false) => new CrearRecogidaImprevistaDTO
        {
            Clave = CLAVE.ToString(), FechaHora = AHORA, EnvioParada = 251300, Empresa = "1", Bultos = 2, Texto = "2 cajas de devolución", AceptaRetornoVivo = acepta
        };

        // ---------------- Comprobar ----------------

        [TestMethod]
        public async Task Comprobar_PorParadaSinRetorno()
        {
            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> r = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { EnvioParada = 251300, Empresa = "1" });

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual("15191", r.Contenido.Cliente, "Sale del envío de la parada");
            Assert.AreEqual("0", r.Contenido.Contacto);
            Assert.AreEqual("CENTRO DE ESTÉTICA LUNA", r.Contenido.NombreCliente);
            Assert.IsFalse(r.Contenido.HayRetornoVivo);
            StringAssert.Contains(r.Contenido.Mensaje, "puede recogerlo");
        }

        [TestMethod]
        public async Task Comprobar_PorClienteConRetornoVivo()
        {
            ConRetornoVivo();

            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> r = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Empresa = "1", Cliente = "15191", Contacto = "0" });

            Assert.IsTrue(r.Contenido.HayRetornoVivo);
            RetornoVivoDTO retorno = r.Contenido.RetornosVivos.Single();
            Assert.AreEqual(250900, retorno.Envio);
            Assert.AreEqual("CTT", retorno.Agencia);
            Assert.AreEqual(930100, retorno.Pedido);
            Assert.AreEqual("En camino", retorno.Estado);
            Assert.AreEqual(2, retorno.Bultos);
            StringAssert.Contains(r.Contenido.Mensaje, "Si se lleva el paquete, la agencia no le entregará el pedido.");
        }

        [TestMethod]
        public async Task Comprobar_SinRetornoEntregadoOReparto_NoCuentan()
        {
            ConRetornoVivo(retorno: 0);
            repositorio.Envios.Add(new EnviosAgencia { Numero = 250901, Empresa = "1", Agencia = 9, Cliente = "15191", Contacto = "0", Estado = 2, Retorno = 1, Fecha = AHORA.Date });
            repositorio.Envios.Add(new EnviosAgencia { Numero = 250902, Empresa = "1", Agencia = 14, Cliente = "15191", Contacto = "0", Estado = 1, Retorno = 1, Fecha = AHORA.Date });
            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> r = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Cliente = "15191", Contacto = "0" });
            Assert.IsFalse(r.Contenido.HayRetornoVivo);
        }

        [TestMethod]
        public async Task Comprobar_AlRefrescarResultaEntregado_YaNoEsVivo()
        {
            ConRetornoVivo();
            refresco.NuevoEstado = 2;
            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> r = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Cliente = "15191", Contacto = "0" });
            Assert.IsFalse(r.Contenido.HayRetornoVivo);
        }

        [TestMethod]
        public async Task Comprobar_AgenciaQueNoContesta_EstadoGuardadoYLoDice()
        {
            ConRetornoVivo();
            refresco.Contesta = false;
            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> r = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Cliente = "15191", Contacto = "0" });
            Assert.IsTrue(r.Contenido.HayRetornoVivo);
            StringAssert.Contains(r.Contenido.Mensaje, "No se ha podido consultar ahora a CTT: se usa el último estado conocido.");
        }

        [TestMethod]
        public async Task Comprobar_Errores()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO())).Codigo);
            ResultadoRecogidaImprevista<ComprobacionRecogidaImprevistaDTO> sinEnvio = await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { EnvioParada = 1 });
            Assert.AreEqual(HttpStatusCode.NotFound, sinEnvio.Codigo);
            StringAssert.Contains(sinEnvio.Mensaje, "No existe el envío 1");
            Assert.AreEqual(HttpStatusCode.NotFound, (await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Cliente = "99999", Contacto = "0" })).Codigo);
            repositorio.Script = false;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Servicio().Comprobar(new ComprobarRecogidaImprevistaDTO { Cliente = "15191" })).Codigo);
        }

        // ---------------- Crear ----------------

        [TestMethod]
        public async Task Crear_NotaYBultosEnLaFurgonetaDelRepartidor_SinParada()
        {
            ResultadoRecogidaImprevista<RecogidaImprevistaDTO> r = await Servicio().Crear(Peticion(), "NUEVAVISION\\Andres");

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo);
            Assert.AreEqual(930500, r.Contenido.PedidoNota);
            Assert.AreEqual("NR930500", r.Contenido.CodigoDocumento);
            CollectionAssert.AreEqual(new[] { "NR930500-1", "NR930500-2" }, r.Contenido.CodigosBultos);
            Assert.AreEqual("15191", r.Contenido.Cliente);
            Assert.IsFalse(r.Contenido.AvisoUrgenteEnviado);
            Assert.AreEqual(2, repositorio.Movimientos.Count);
            Assert.IsTrue(repositorio.Movimientos.All(m => m.Tipo == TiposMovimientoBulto.RECOGIDO && m.Usuario == "NUEVAVISION\\Andres"
                && m.TipoCodigo == CodigoBultoFurgoneta.NR && m.Pedido == 930500 && m.EnvioId == null && m.Total == 2));
            Assert.AreEqual(2, repositorio.Movimientos.Select(m => m.ClaveIdempotencia).Distinct().Count());
            Assert.AreEqual(0, avisos.Avisos.Single().Retornos, "Solo la campana normal a Almacén");
        }

        [TestMethod]
        public async Task Crear_MismaClave_DevuelveLoMismoSinCrearOtra()
        {
            RecogidaImprevistaDTO primera = (await Servicio().Crear(Peticion(), "Andres")).Contenido;
            ResultadoRecogidaImprevista<RecogidaImprevistaDTO> segunda = await Servicio().Crear(Peticion(), "Andres");

            Assert.AreEqual(1, repositorio.Creadas);
            Assert.AreEqual(HttpStatusCode.Created, segunda.Codigo);
            Assert.AreEqual(primera.CodigoDocumento, segunda.Contenido.CodigoDocumento);
            CollectionAssert.AreEqual(primera.CodigosBultos, segunda.Contenido.CodigosBultos);
            Assert.AreEqual(primera.NombreCliente, segunda.Contenido.NombreCliente);
            Assert.AreEqual(1, avisos.Avisos.Count, "No se avisa dos veces");
        }

        [TestMethod]
        public async Task Crear_ConRetornoVivoSinAceptar_409YNoCreaNada()
        {
            ConRetornoVivo();

            ResultadoRecogidaImprevista<RecogidaImprevistaDTO> r = await Servicio().Crear(Peticion(), "Andres");

            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
            Assert.AreEqual("RETORNO_VIVO", r.CodigoError);
            Assert.AreEqual(250900, r.RetornosVivos.Single().Envio);
            Assert.AreEqual(0, repositorio.Creadas);
            Assert.AreEqual(0, avisos.Avisos.Count);
        }

        [TestMethod]
        public async Task Crear_ConRetornoVivoAceptado_AvisoUrgenteEHistoria()
        {
            ConRetornoVivo();
            CrearRecogidaImprevistaDTO peticion = Peticion(acepta: true);
            peticion.Clave = Guid.NewGuid().ToString();

            ResultadoRecogidaImprevista<RecogidaImprevistaDTO> r = await Servicio().Crear(peticion, "Andres");

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo);
            Assert.IsTrue(r.Contenido.AvisoUrgenteEnviado);
            Assert.AreEqual(true, repositorio.AvisoUrgenteGuardado);
            Assert.AreEqual(1, avisos.Avisos.Single().Retornos);
            Assert.AreEqual(250900, repositorio.Historia.Single().Envio);
            StringAssert.Contains(repositorio.Historia.Single().Texto, "NR930500");
        }

        [TestMethod]
        public async Task Crear_Validaciones()
        {
            CrearRecogidaImprevistaDTO sinClave = Peticion();
            sinClave.Clave = "no-es-un-guid";
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Crear(sinClave, "Andres")).Codigo);
            CrearRecogidaImprevistaDTO bultos = Peticion();
            bultos.Bultos = 100;
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Crear(bultos, "Andres")).Codigo);
            CrearRecogidaImprevistaDTO sinTexto = Peticion();
            sinTexto.Texto = " ";
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Crear(sinTexto, "Andres")).Codigo);
            CrearRecogidaImprevistaDTO largo = Peticion();
            largo.Texto = new string('a', 501);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Crear(largo, "Andres")).Codigo);
            CrearRecogidaImprevistaDTO foto = Peticion();
            foto.FotoBase64 = "###";
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Servicio().Crear(foto, "Andres")).Codigo);
            Assert.AreEqual(0, repositorio.Creadas);
            repositorio.Script = false;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Servicio().Crear(Peticion(), "Andres")).Codigo);
        }

        // ---------------- Avisos y urgente ----------------

        [TestMethod]
        public void Avisos_CampanaYUrgenteConReferenciaDelEnvio()
        {
            var recogida = new RecogidaImprevistaDTO { PedidoNota = 930500, CodigoDocumento = "NR930500", CodigosBultos = new List<string> { "NR930500-1" }, Empresa = "1", Cliente = "15191", Contacto = "0", NombreCliente = "LUNA" };
            List<NotificacionPushDTO> avisosCompuestos = AvisosRecogidaImprevista.Componer(recogida, "Andres",
                new List<RetornoVivoDTO> { new RetornoVivoDTO { Envio = 250900, Agencia = "CTT", Pedido = 930100 } });

            Assert.AreEqual("RecogidaImprevista", avisosCompuestos[0].Tipo);
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(avisosCompuestos[0]));
            NotificacionPushDTO urgente = avisosCompuestos[1];
            Assert.AreEqual("RecogidaConRetornoEnCamino", urgente.Tipo);
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(urgente));
            Assert.AreEqual("250900", urgente.Datos["referencia"]);
            StringAssert.Contains(urgente.Cuerpo, "Llame a CTT");
        }

        [TestMethod]
        public async Task Urgente_PendienteMientrasElEnvioSigaVivo()
        {
            var comprobador = new ComprobadorRecogidasConRetornoEnCamino(ids => Task.FromResult(new Dictionary<int, short> { [250900] = 1, [250901] = 2 }));
            ISet<string> pendientes = await comprobador.Pendientes(new[] { "250900", "250901", "x" });
            CollectionAssert.AreEqual(new[] { "250900" }, pendientes.ToArray());
        }

        // ---------------- Permisos ----------------

        [TestMethod]
        public async Task Controlador_SinGrupoDeReparto_403()
        {
            var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma"), new Claim(ClaimTypes.Role, "NUEVAVISION\\Tiendas") }, "prueba"));
            var controlador = new RepartoPropioController(A.Fake<NVEntities>(), new ServicioRepartoPropio(new NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio.ServicioRepartoPropioTests.RepositorioFalso(), () => AHORA))
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                CrearServicioImprevistas = () => Servicio()
            };

            Assert.AreEqual(HttpStatusCode.Forbidden, (await (await controlador.CrearRecogidaImprevista(Peticion())).ExecuteAsync(CancellationToken.None)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await (await controlador.ComprobarRecogidaImprevista(new ComprobarRecogidaImprevistaDTO { Cliente = "15191" })).ExecuteAsync(CancellationToken.None)).StatusCode);
            Assert.AreEqual(0, repositorio.Creadas);
        }

        [TestMethod]
        public async Task Controlador_409ConCodigoYRetornosEnLaRaiz()
        {
            ConRetornoVivo();
            var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Andres"), new Claim(ClaimTypes.Role, "NUEVAVISION\\Almacén") }, "prueba"));
            var controlador = new RepartoPropioController(A.Fake<NVEntities>(), new ServicioRepartoPropio(new NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio.ServicioRepartoPropioTests.RepositorioFalso(), () => AHORA))
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                CrearServicioImprevistas = () => Servicio()
            };

            HttpResponseMessage respuesta = await (await controlador.CrearRecogidaImprevista(Peticion())).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            string json = await respuesta.Content.ReadAsStringAsync();
            StringAssert.Contains(json, "\"Codigo\":\"RETORNO_VIVO\"");
            StringAssert.Contains(json, "\"RetornosVivos\":[{\"Envio\":250900");
            StringAssert.Contains(json, "\"Message\":");
        }
    }
}
