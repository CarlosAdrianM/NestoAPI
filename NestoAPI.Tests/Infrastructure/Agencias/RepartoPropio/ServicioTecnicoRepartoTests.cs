using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
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

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881 (corte 2): el tipo de servicio técnico de las notas de entrega y de recogida.</summary>
    [TestClass]
    public class ServicioTecnicoRepartoTests
    {
        private RepositorioFalso repositorio;
        private IRepositorioNotasEntregaCabecera notas;
        private ServicioServicioTecnicoReparto servicio;

        [TestInitialize]
        public void Preparar()
        {
            ServicioServicioTecnicoReparto.OlvidarAvisos();
            repositorio = new RepositorioFalso();
            notas = A.Fake<IRepositorioNotasEntregaCabecera>();
            A.CallTo(() => notas.LeerPedido("1", 930100)).Returns(new PedidoNotaEntrega { Cliente = "1001", NotaEntrega = true });
            A.CallTo(() => notas.Leer("1", 930100)).Returns(new FilaNotaEntregaCabecera { Empresa = "1", Pedido = 930100, Tipo = TiposNotaEntrega.RECOGIDA });
            A.CallTo(() => notas.LeerPedido("1", 930200)).Returns(new PedidoNotaEntrega { Cliente = "1001", NotaEntrega = false });
            A.CallTo(() => notas.LeerPedido("1", 1)).Returns(Task.FromResult<PedidoNotaEntrega>(null));
            servicio = new ServicioServicioTecnicoReparto(repositorio, notas, () => ConstantesTiemposReparto.PorDefecto());
        }

        // ---------------------------------------------------------------- regla

        [TestMethod]
        public void Regla_Nombre_Minutos_YLosQueCuentan()
        {
            Assert.AreEqual("Instalar sillón", ReglaServicioTecnico.Normalizar("  Instalar   sillón "));
            Assert.IsNotNull(ReglaServicioTecnico.MotivoNombre(" "));
            Assert.IsNotNull(ReglaServicioTecnico.MotivoNombre(new string('x', 51)));
            Assert.IsNull(ReglaServicioTecnico.MotivoNombre(new string('x', 50)));
            Assert.IsNotNull(ReglaServicioTecnico.MotivoMinutos(0));
            Assert.IsNotNull(ReglaServicioTecnico.MotivoMinutos(601));
            Assert.IsNull(ReglaServicioTecnico.MotivoMinutos(null));
            Assert.AreEqual(90, ReglaServicioTecnico.MinutosQueCuentan(90, 45, 60), "Los tecleados mandan");
            Assert.AreEqual(45, ReglaServicioTecnico.MinutosQueCuentan(null, 45, 60), "Si no, los del tipo");
            Assert.AreEqual(60, ReglaServicioTecnico.MinutosQueCuentan(null, null, 60), "Si no, los de por defecto");
            Assert.IsNotNull(ReglaServicioTecnico.Motivo(new PeticionServicioTecnicoNotaDTO { MinutosServicio = 30 }), "Minutos sin tipo: no");
            Assert.IsTrue(ReglaServicioTecnico.SinTipo(new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "  " }));
        }

        // ---------------------------------------------------------------- tipos

        [TestMethod]
        public async Task Tipos_SinScript_ListaVacia_YCrearDa503()
        {
            repositorio.Script = false;

            Assert.AreEqual(0, (await servicio.Tipos()).Contenido.Count);
            ResultadoServicioTecnico<TipoServicioTecnicoDTO> crear = await servicio.CrearTipo(new CrearTipoServicioTecnicoDTO { Nombre = "Montaje" }, "Alfredo");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, crear.Estado);
        }

        [TestMethod]
        public async Task CrearTipo_NombreRepetido_DevuelveElQueHay_SinDuplicar()
        {
            TipoServicioTecnicoDTO uno = (await servicio.CrearTipo(new CrearTipoServicioTecnicoDTO { Nombre = "Instalar sillón", MinutosEstimados = 90 }, "Alfredo")).Contenido;
            TipoServicioTecnicoDTO dos = (await servicio.CrearTipo(new CrearTipoServicioTecnicoDTO { Nombre = "  instalar   SILLÓN " }, "Santiago")).Contenido;

            Assert.AreEqual(uno.Id, dos.Id);
            Assert.AreEqual(1, repositorio.Tipos.Count);
            Assert.AreEqual(90, dos.MinutosEstimados, "No cambia sus minutos");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.CrearTipo(new CrearTipoServicioTecnicoDTO { Nombre = "" }, "Alfredo")).Estado);
        }

        // ---------------------------------------------------------------- nota

        [TestMethod]
        public async Task Poner_PorNombre_CreaElTipo_ConMinutosTecleados_YDejaRastro()
        {
            ResultadoServicioTecnico<ServicioTecnicoNotaDTO> r = await servicio.Poner("1", 930100,
                new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "Revisión de máquina", MinutosServicio = 40 }, "Alfredo");

            Assert.AreEqual(HttpStatusCode.OK, r.Estado, r.Mensaje);
            Assert.AreEqual("Revisión de máquina", r.Contenido.ServicioTecnico);
            Assert.AreEqual(40, r.Contenido.MinutosServicio);
            Assert.AreEqual(40, r.Contenido.MinutosEstimados);
            Assert.AreEqual(1, repositorio.Tipos.Count);
            Assert.AreEqual(1, repositorio.Rastros.Count);
        }

        [TestMethod]
        public async Task Poner_SinMinutos_ElDelTipo_OSinEl60_YQuitarlo()
        {
            TipoServicioTecnicoDTO tipo = await repositorio.CrearTipo("Montaje", null, "x");

            ServicioTecnicoNotaDTO puesto = (await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoId = tipo.Id }, "Alfredo")).Contenido;
            Assert.AreEqual(60, puesto.MinutosEstimados);
            Assert.IsNull(puesto.MinutosServicio);

            ServicioTecnicoNotaDTO quitado = (await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO(), "Alfredo")).Contenido;
            Assert.IsNull(quitado.TipoServicioTecnicoId);
            Assert.IsNull(quitado.MinutosEstimados);
            Assert.AreEqual(2, repositorio.Rastros.Count);

            _ = await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO(), "Alfredo");
            Assert.AreEqual(2, repositorio.Rastros.Count, "Sin cambios, sin rastro");
        }

        [TestMethod]
        public async Task Poner_NoEsNota400_NoExiste404_Entregada409_Cobro400_TipoInexistente400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Poner("1", 930200, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "X" }, "a")).Estado);
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Poner("1", 1, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "X" }, "a")).Estado);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoId = 999 }, "a")).Estado);

            A.CallTo(() => notas.LeerUltimoEnvio("1", 930100)).Returns(new EnvioNotaEntrega { Numero = 251000, Estado = Constantes.Agencias.ESTADO_ENTREGADO });
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "X" }, "a")).Estado);

            A.CallTo(() => notas.LeerPedido("1", 930300)).Returns(new PedidoNotaEntrega { NotaEntrega = true });
            A.CallTo(() => notas.Leer("1", 930300)).Returns(new FilaNotaEntregaCabecera { Tipo = TiposNotaEntrega.COBRO });
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Poner("1", 930300, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "X" }, "a")).Estado);
            Assert.AreEqual(0, repositorio.Rastros.Count);
        }

        [TestMethod]
        public async Task Completar_LasParadasQueSonNotas_TraenElServicioTecnicoYSusMinutos()
        {
            TipoServicioTecnicoDTO tipo = await repositorio.CrearTipo("Montaje", 45, "x");
            _ = await servicio.Poner("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoId = tipo.Id }, "Alfredo");
            var paradas = new List<EnvioRepartoDTO>
            {
                new EnvioRepartoDTO { EnvioId = 1, Pedido = 930100, TipoParada = TiposParadaReparto.NOTA_RECOGIDA },
                new EnvioRepartoDTO { EnvioId = 2, Pedido = 930100, TipoParada = TiposParadaReparto.ENVIO },
                new EnvioRepartoDTO { EnvioId = 3, Pedido = 930555, TipoParada = TiposParadaReparto.NOTA_ENTREGA }
            };

            await servicio.Completar(paradas);

            Assert.AreEqual("Montaje", paradas[0].ServicioTecnico);
            Assert.AreEqual(45, paradas[0].MinutosServicio);
            Assert.IsNull(paradas[1].ServicioTecnico, "Un envío normal no lleva servicio técnico");
            Assert.IsNull(paradas[2].ServicioTecnico);
            Assert.AreEqual(45d, MinutosParadaReparto.De(ParadaParaTiempo.De(paradas[0]), ConstantesTiemposReparto.PorDefecto()), "Entra en el fin de ruta");
        }

        [TestMethod]
        public async Task NotaNueva_ElPostDelPedido_ValidaAntes_YLoGuardaDespues()
        {
            var cabeceras = A.Fake<IRepositorioNotasEntregaCabecera>();
            A.CallTo(() => cabeceras.ScriptAplicado()).Returns(true);
            var notasCabecera = new ServicioNotasEntregaCabecera(cabeceras) { ServicioTecnico = servicio };
            var pedido = new PedidoVentaDTO
            {
                empresa = "1", numero = 930100, notaEntrega = true, tipoNotaEntrega = TiposNotaEntrega.RECOGIDA, bultosNota = 1,
                tipoServicioTecnicoNombre = "Instalar sillón", minutosServicio = 0,
                Lineas = new List<LineaPedidoVentaDTO>()
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Alfredo"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN) }, "Prueba"));

            StringAssert.Contains(await notasCabecera.PrepararAlCrear(pedido, principal), "entre 1 y 600");

            pedido.minutosServicio = 75;
            Assert.IsNull(await notasCabecera.PrepararAlCrear(pedido, principal));
            await notasCabecera.GuardarAlCrear(pedido, "Alfredo");

            ServicioTecnicoNotaDTO guardado = (await servicio.Leer("1", 930100)).Contenido;
            Assert.AreEqual("Instalar sillón", guardado.ServicioTecnico);
            Assert.AreEqual(75, guardado.MinutosEstimados);
        }

        [TestMethod]
        public void Controlador_SinPermiso_403()
        {
            var controlador = new ServicioTecnicoRepartoController(null, () => servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Vendedor") }, "Prueba"))
            };

            HttpResponseMessage respuesta = controlador.GetTipos().Result.ExecuteAsync(CancellationToken.None).Result;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        [TestMethod]
        public void Controlador_Almacen_CreaYLee_YAvisaAlRepartoSoloSiSeGuarda()
        {
            var avisos = new List<string>();
            var controlador = new ServicioTecnicoRepartoController(null, () => servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Alfredo"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN) }, "Prueba")),
                AvisarReparto = u => avisos.Add(u)
            };

            HttpResponseMessage creado = controlador.PostTipo(new CrearTipoServicioTecnicoDTO { Nombre = "Montaje" }).Result.ExecuteAsync(CancellationToken.None).Result;
            HttpResponseMessage puesto = controlador.PutServicioTecnicoNota("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "Montaje" })
                .Result.ExecuteAsync(CancellationToken.None).Result;
            HttpResponseMessage mal = controlador.PutServicioTecnicoNota("1", 930100, new PeticionServicioTecnicoNotaDTO { TipoServicioTecnicoNombre = "Montaje", MinutosServicio = 9999 })
                .Result.ExecuteAsync(CancellationToken.None).Result;

            Assert.AreEqual(HttpStatusCode.OK, creado.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, puesto.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, mal.StatusCode);
            // NestoAPI#881: cambian los minutos de la parada → «repartoCambiado» (Motivo «Parada»); el 400 no avisa
            CollectionAssert.AreEqual(new[] { "Alfredo" }, avisos);
        }

        // ================================================================ dobles

        internal class RepositorioFalso : IRepositorioServicioTecnico
        {
            public bool Script = true;
            public readonly List<TipoServicioTecnicoDTO> Tipos = new List<TipoServicioTecnicoDTO>();
            public readonly Dictionary<int, (int? Tipo, int? Minutos)> Notas = new Dictionary<int, (int?, int?)>();
            public readonly List<(string Anterior, string Nuevo)> Rastros = new List<(string, string)>();

            public bool ScriptAplicado() => Script;

            public Task<List<TipoServicioTecnicoDTO>> LeerTipos(bool soloActivos)
                => Task.FromResult(Tipos.Where(t => !soloActivos || t.Activo).OrderBy(t => t.Nombre).ToList());

            public Task<TipoServicioTecnicoDTO> LeerTipo(int id) => Task.FromResult(Tipos.FirstOrDefault(t => t.Id == id));

            public Task<TipoServicioTecnicoDTO> LeerTipoPorNombre(string nombre)
                => Task.FromResult(Tipos.FirstOrDefault(t => string.Equals(t.Nombre, nombre, StringComparison.CurrentCultureIgnoreCase)));

            public Task<TipoServicioTecnicoDTO> CrearTipo(string nombre, int? minutos, string usuario)
            {
                var tipo = new TipoServicioTecnicoDTO { Id = Tipos.Count + 1, Nombre = nombre, MinutosEstimados = minutos, Activo = true };
                Tipos.Add(tipo);
                return Task.FromResult(tipo);
            }

            public Task<Dictionary<int, FilaServicioTecnicoNota>> LeerDeNotas(string empresa, IReadOnlyCollection<int> pedidos)
                => Task.FromResult(pedidos.Where(p => Notas.ContainsKey(p) && Notas[p].Tipo.HasValue).ToDictionary(p => p, p => new FilaServicioTecnicoNota
                {
                    Pedido = p,
                    TipoServicioTecnicoId = Notas[p].Tipo,
                    Nombre = Tipos.FirstOrDefault(t => t.Id == Notas[p].Tipo)?.Nombre,
                    MinutosTipo = Tipos.FirstOrDefault(t => t.Id == Notas[p].Tipo)?.MinutosEstimados,
                    MinutosServicio = (short?)Notas[p].Minutos
                }));

            public Task Guardar(string empresa, int pedido, int? tipoId, int? minutos, string usuario, string anterior, string nuevo)
            {
                Notas[pedido] = (tipoId, minutos);
                Rastros.Add((anterior, nuevo));
                return Task.CompletedTask;
            }
        }
    }
}
