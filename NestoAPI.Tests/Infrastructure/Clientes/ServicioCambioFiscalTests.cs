using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.CambioFiscal;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#876: previa, copia y paso de pedidos de un cliente que cambia de forma fiscal (repositorio falso).</summary>
    [TestClass]
    public class ServicioCambioFiscalTests
    {
        private RepositorioFalso repo;
        private ServicioCambioFiscal servicio;
        private List<string> publicados;

        private class RepositorioFalso : IRepositorioCambioFiscal
        {
            public bool HayTabla = true;
            public DatosCambioFiscal Datos;
            public DecisionCopiaCambioFiscal Escrita;
            public int Copias;
            public List<string> Enlaces = new List<string>();

            public Task<bool> TablaExiste() => Task.FromResult(HayTabla);
            public Task<DatosCambioFiscal> Leer(string empresa, string anterior, string nuevo) => Task.FromResult(Datos);
            public Task<DecisionCopiaCambioFiscal> Copiar(string empresa, string anterior, string nuevo, Func<DatosCambioFiscal, DecisionCopiaCambioFiscal> decidir, string usuario)
            {
                Copias++;
                DecisionCopiaCambioFiscal d = decidir(Datos);
                if (d.Escribir)
                {
                    Escrita = d;
                }
                return Task.FromResult(d);
            }
            public Task CrearEnlace(string empresa, string nuevo, string anterior, string usuario)
            {
                Enlaces.Add($"{nuevo}<-{anterior}");
                return Task.CompletedTask;
            }
        }

        [TestInitialize]
        public void Setup()
        {
            repo = new RepositorioFalso
            {
                Datos = new DatosCambioFiscal
                {
                    Anterior = new FotoClienteCambioFiscal
                    {
                        Cliente = "37283",
                        Fichas = new List<FichaCambioFiscal> { new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true, Estado = 67, Vendedor = "CAM", Iva = "E52", DiasEnServir = "11111" } }
                    },
                    Nuevo = new FotoClienteCambioFiscal
                    {
                        Cliente = "42178",
                        Fichas = new List<FichaCambioFiscal> { new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true, Estado = 0, Vendedor = "NV", Iva = "G21", DiasEnServir = "11111" } }
                    },
                    OrigenNuevo = "CAMBIO-FISCAL"
                }
            };
            servicio = new ServicioCambioFiscal(repo) { Hoy = () => new DateTime(2026, 11, 26), RegistrarEnElmah = _ => { } };
            publicados = new List<string>();
        }

        private Task<ResultadoCambioFiscal<CopiaCambioFiscalDTO>> Copiar(bool ensayo, Func<string, Task<string>> permiso = null) =>
            servicio.Copiar(new CopiaCambioFiscalRequest { Empresa = "1", ClienteAnterior = "37283", ClienteNuevo = "42178" }, ensayo, "u",
                permiso ?? (_ => Task.FromResult<string>(null)), (e, c) => { publicados.Add(c); return Task.CompletedTask; });

        [TestMethod]
        public async Task Copiar_SinTabla_503()
        {
            repo.HayTabla = false;

            Assert.AreEqual(503, (await Copiar(false)).Estado);
        }

        [TestMethod]
        public async Task Copiar_Ensayo_NoEscribeNiPublica()
        {
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await Copiar(true);

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Ensayo);
            Assert.IsTrue(r.Datos.HayCambios);
            Assert.IsTrue(r.Datos.SeEnlazaAlCopiar);
            Assert.AreEqual(0, repo.Copias);
            Assert.AreEqual(0, publicados.Count);
        }

        [TestMethod]
        public async Task Copiar_DeVerdad_CreaElEnlaceEscribeConRastroYPublica()
        {
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await Copiar(false);

            Assert.AreEqual(200, r.Estado, r.Motivo);
            Assert.IsNotNull(repo.Escrita);
            Assert.IsTrue(repo.Escrita.CrearEnlace);
            Assert.IsNotNull(repo.Escrita.Rastro);
            Assert.AreEqual("E52", repo.Escrita.Plan.Fichas.Single().Iva);
            CollectionAssert.AreEqual(new[] { "42178" }, publicados);
            Assert.IsTrue(r.Datos.Enlazados);
        }

        [TestMethod]
        public async Task Copiar_YaTieneTodoYEstanEnlazados_NoEscribeNada()
        {
            repo.Datos.Nuevo.Fichas[0].Estado = 67;
            repo.Datos.Nuevo.Fichas[0].Vendedor = "CAM";
            repo.Datos.Nuevo.Fichas[0].Iva = "E52";
            repo.Datos.AnterioresDelNuevo = new List<string> { "37283" };

            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await Copiar(false);

            Assert.AreEqual(200, r.Estado);
            Assert.IsNull(repo.Escrita);
            Assert.AreEqual(0, publicados.Count);
            StringAssert.Contains(r.Datos.Mensaje, "no se ha cambiado nada");
        }

        [TestMethod]
        public async Task Copiar_NoEsCambioFiscal_409SinAbrirTransaccion()
        {
            repo.Datos.OrigenNuevo = "WEB";

            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await Copiar(false);

            Assert.AreEqual(409, r.Estado);
            Assert.AreEqual(ResultadoCambioFiscal<CopiaCambioFiscalDTO>.NO_ES_CAMBIO_FISCAL, r.Codigo);
            Assert.AreEqual(0, repo.Copias);
        }

        [TestMethod]
        public async Task Copiar_SinPermiso_403()
        {
            string vendedorVisto = null;
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await Copiar(false, v => { vendedorVisto = v; return Task.FromResult("no"); });

            Assert.AreEqual(403, r.Estado);
            Assert.AreEqual("CAM", vendedorVisto, "El permiso se mira con el vendedor del anterior");
            Assert.AreEqual(0, repo.Copias);
        }

        [TestMethod]
        public async Task Copiar_BloqueQueNoExiste_400()
        {
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await servicio.Copiar(new CopiaCambioFiscalRequest
            {
                ClienteAnterior = "37283", ClienteNuevo = "42178", Bloques = new List<string> { "Iban" }
            }, false, "u", _ => Task.FromResult<string>(null), (e, c) => Task.CompletedTask);

            Assert.AreEqual(400, r.Estado);
            StringAssert.Contains(r.Motivo, "Iban");
        }

        [TestMethod]
        public async Task Copiar_OdooFalla_CopiaIgualYLoDice()
        {
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await servicio.Copiar(new CopiaCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178" },
                false, "u", _ => Task.FromResult<string>(null), (e, c) => throw new InvalidOperationException("Odoo"));

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Avisos.Any(a => a.Contains("Odoo")));
        }

        [TestMethod]
        public async Task Previa_TraeLosBloquesYLosPedidos()
        {
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await servicio.Previa("1", "37283", "42178", _ => Task.FromResult<string>(null),
                () => Task.FromResult(new List<PedidoPendienteCambioFiscalDTO> { new PedidoPendienteCambioFiscalDTO { Pedido = 931691 } }));

            Assert.AreEqual(200, r.Estado);
            Assert.AreEqual(BloquesCambioFiscal.TODOS.Count, r.Datos.Bloques.Count);
            Assert.AreEqual(931691, r.Datos.PedidosPendientes.Single().Pedido);
            Assert.AreEqual(0, repo.Copias);
        }

        [TestMethod]
        public async Task PasarPedidos_PasaLosSuyosYRechazaLosDeOtro()
        {
            List<CambiarClientePedidoRequest> peticiones = new List<CambiarClientePedidoRequest>();
            ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO> r = await servicio.PasarPedidos(
                new PasarPedidosCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178", Pedidos = new List<int> { 931691, 931700, 931701, 931702 } },
                false, "u", _ => Task.FromResult<string>(null),
                n => Task.FromResult(n == 931700 ? Tuple.Create("11111", "0") : n == 931702 ? null : Tuple.Create("37283", "0")),
                (n, p) =>
                {
                    peticiones.Add(p);
                    return Task.FromResult(n == 931701
                        ? new ResultadoCambioClientePedido { Error = "cambiaría el precio", Codigo = ReglaCambioClienteConPicking.CODIGO_CAMBIARIA }
                        : new ResultadoCambioClientePedido { Respuesta = new CambiarClientePedidoRespuesta { TeniaPicking = true, SinCambiosDeImportes = true } });
                });

            Assert.AreEqual(200, r.Estado, r.Motivo);
            CollectionAssert.AreEqual(new[] { "42178<-37283" }, repo.Enlaces, "Sin enlace y con origen CAMBIO-FISCAL, se crea");
            Assert.AreEqual(1, r.Datos.Pasados);
            Assert.IsTrue(r.Datos.Pedidos.Single(p => p.Pedido == 931691).TeniaPicking);
            Assert.AreEqual("NO_ES_DEL_CLIENTE_ANTERIOR", r.Datos.Pedidos.Single(p => p.Pedido == 931700).Codigo);
            Assert.AreEqual(ReglaCambioClienteConPicking.CODIGO_CAMBIARIA, r.Datos.Pedidos.Single(p => p.Pedido == 931701).Codigo);
            Assert.AreEqual("NO_EXISTE", r.Datos.Pedidos.Single(p => p.Pedido == 931702).Codigo);
            Assert.AreEqual(2, peticiones.Count);
            Assert.IsTrue(peticiones.All(p => p.Cliente == "42178" && p.Contacto == "0" && !p.Ensayo));
        }

        [TestMethod]
        public async Task PasarPedidos_Ensayo_NoCreaElEnlaceYPideEnsayo()
        {
            CambiarClientePedidoRequest vista = null;
            ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO> r = await servicio.PasarPedidos(
                new PasarPedidosCambioFiscalRequest { ClienteAnterior = "37283", ClienteNuevo = "42178", Pedidos = new List<int> { 931691 } },
                true, "u", _ => Task.FromResult<string>(null), n => Task.FromResult(Tuple.Create("37283", "5")),
                (n, p) => { vista = p; return Task.FromResult(new ResultadoCambioClientePedido { Respuesta = new CambiarClientePedidoRespuesta { Ensayo = true } }); });

            Assert.AreEqual(200, r.Estado);
            Assert.AreEqual(0, repo.Enlaces.Count);
            Assert.IsTrue(vista.Ensayo);
            Assert.IsNull(vista.Contacto, "El nuevo no tiene el contacto 5: va a su principal");
        }
    }
}
