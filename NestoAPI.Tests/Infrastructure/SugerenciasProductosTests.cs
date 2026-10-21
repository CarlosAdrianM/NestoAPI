using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#670 (corte 1 de la épica #669): sugerencias de productos en la plantilla de venta por reglas, con el registro
    /// de lo mostrado, lo respondido y lo que entra en el pedido. Datos inventados; sin BD.
    /// </summary>
    [TestClass]
    public partial class SugerenciasProductosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 19, 10, 0, 0);
        private const string CLIENTE = "1001";

        private IRepositorioSugerenciasProductos _repo;
        private Dictionary<Guid, SesionSugerencias> _sesiones;
        private List<SugerenciaMostrada> _mostradas;
        private List<ParCompradoJunto> _pares;
        private List<CompraDiaReposicion> _compras;
        private List<CandidatoPotenciar> _potenciar;
        private List<AfinidadCliente> _afinidades;
        private Dictionary<string, int> _clientesPerfil;
        private Dictionary<string, FichaProductoSugerencia> _fichas;
        private HashSet<string> _sinStock;
        private HashSet<string> _pendientes;
        private HashSet<string> _rechazados;
        /// <summary>NestoAPI#669: (rechazo, cliente, FechaRespuesta).</summary>
        private List<Tuple<RechazoCliente, string, DateTime>> _rechazosConFamilia;
        private DatosGrupoVendedor _datosVendedor;
        private bool _existenTablas;
        private bool _admiteDescartadoVendedor;
        private int _siguienteId;
        private List<Exception> _errores;
        private ServicioSugerenciasProductos _servicio;

        [TestInitialize]
        public void Setup()
        {
            _existenTablas = true;
            _admiteDescartadoVendedor = true;
            _sesiones = new Dictionary<Guid, SesionSugerencias>();
            _mostradas = new List<SugerenciaMostrada>();
            _siguienteId = 100;
            _errores = new List<Exception>();
            _sinStock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _pendientes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _rechazados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _rechazosConFamilia = new List<Tuple<RechazoCliente, string, DateTime>>();
            _datosVendedor = new DatosGrupoVendedor { EstadoVendedor = 0, VendedorEsteticaCliente = "SAN", VendedorPeluqueriaCliente = "PEL" };

            _fichas = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in new[] { "CARRO1", "JUNTO1", "JUNTO2", "LETOCA1", "LETOCA2", "POT1", "POT2", "POT3" })
            {
                Ficha(p, "COS", "CRE");
            }
            Ficha("PELU1", Constantes.Productos.GRUPO_PELUQUERIA, "CHA");

            _pares = new List<ParCompradoJunto>
            {
                Par("CARRO1", "PELU1", 0.9m),
                Par("CARRO1", "JUNTO1", 0.4m),
                Par("CARRO1", "JUNTO2", 0.3m)
            };
            _compras = new List<CompraDiaReposicion>();
            Compras("LETOCA1", -150, -120, -90, -60);      // cada 30 días; el último hace 60 → 2 veces
            Compras("LETOCA2", -70, -50, -30);             // cada 20 días; el último hace 30 → 1,5 veces
            Compras("CARRO1", -10);
            _potenciar = new List<CandidatoPotenciar>
            {
                new CandidatoPotenciar { Producto = "POT1", Tipo = CandidatoPotenciar.TIPO_OFERTA, Detalle = "6+2" },
                new CandidatoPotenciar { Producto = "POT2", Tipo = CandidatoPotenciar.TIPO_NOVEDAD },
                new CandidatoPotenciar { Producto = "POT3", Tipo = CandidatoPotenciar.TIPO_POTENCIAR }
            };
            _clientesPerfil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "POT1", 12 }, { "POT2", 5 }, { "POT3", 2 } };
            _afinidades = new List<AfinidadCliente>();

            _repo = A.Fake<IRepositorioSugerenciasProductos>();
            A.CallTo(() => _repo.ExistenTablas()).ReturnsLazily(() => Task.FromResult(_existenTablas));
            A.CallTo(() => _repo.LeerSesion(A<Guid>._)).ReturnsLazily((Guid id) => Task.FromResult(_sesiones.TryGetValue(id, out SesionSugerencias s) ? s : null));
            A.CallTo(() => _repo.CrearSesion(A<SesionSugerencias>._)).ReturnsLazily((SesionSugerencias s) =>
            {
                _sesiones[s.Id] = s;
                return Task.CompletedTask;
            });
            A.CallTo(() => _repo.ActualizarSesion(A<Guid>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((Guid id, string contacto, string vendedor, string carrito) =>
                {
                    _sesiones[id].Carrito = carrito;
                    return Task.CompletedTask;
                });
            A.CallTo(() => _repo.LeerMostradas(A<Guid>._)).ReturnsLazily((Guid id) =>
                Task.FromResult(_mostradas.Where(m => m.SesionId == id).Select(Copia).ToList()));
            A.CallTo(() => _repo.LeerMostrada(A<int>._)).ReturnsLazily((int id) => Task.FromResult(_mostradas.Where(m => m.Id == id).Select(Copia).FirstOrDefault()));
            A.CallTo(() => _repo.InsertarMostrada(A<SugerenciaMostrada>._)).ReturnsLazily((SugerenciaMostrada m) =>
            {
                SugerenciaMostrada copia = Copia(m);
                copia.Id = ++_siguienteId;
                _mostradas.Add(copia);
                return Task.FromResult(copia.Id);
            });
            A.CallTo(() => _repo.GuardarRespuesta(A<int>._, A<string>._)).ReturnsLazily((int id, string r) =>
            {
                SugerenciaMostrada m = _mostradas.Single(x => x.Id == id);
                m.Respuesta = r;
                m.FechaRespuesta = HOY;
                return Task.CompletedTask;
            });
            A.CallTo(() => _repo.AdmiteDescartadoVendedor()).ReturnsLazily(() => Task.FromResult(_admiteDescartadoVendedor));
            A.CallTo(() => _repo.GuardarDescartadoVendedorProvisional(A<int>._)).ReturnsLazily((int id) =>
            {
                // Lo mismo que SQL_GUARDAR_DESCARTADO_VENDEDOR_PROVISIONAL
                SugerenciaMostrada m = _mostradas.Single(x => x.Id == id);
                m.Respuesta = RespuestasSugerencia.NO_LE_INTERESA;
                m.FechaRespuesta = HOY;
                m.Texto = RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR + m.Texto;
                return Task.CompletedTask;
            });
            // Lo mismo que SQL_RECHAZADOS: los de ese cliente con FechaRespuesta >= desde
            A.CallTo(() => _repo.LeerRechazados(A<string>._, A<string>._, A<DateTime>._))
                .ReturnsLazily((string e, string cliente, DateTime desde) => Task.FromResult(_rechazados.Select(p => new RechazoCliente { Producto = p })
                    .Concat(_rechazosConFamilia.Where(r => r.Item2 == cliente && r.Item3 >= desde).Select(r => r.Item1)).ToList()));
            A.CallTo(() => _repo.LeerComprasCliente(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(_compras.ToList()));
            A.CallTo(() => _repo.LeerPendientesCliente(A<string>._, A<string>._))
                .ReturnsLazily(() => Task.FromResult(new HashSet<string>(_pendientes, StringComparer.OrdinalIgnoreCase)));
            A.CallTo(() => _repo.LeerPares(A<IEnumerable<string>>._)).ReturnsLazily((IEnumerable<string> carrito) =>
                Task.FromResult(_pares.Where(p => carrito.Contains(p.ProductoA, StringComparer.OrdinalIgnoreCase)).ToList()));
            A.CallTo(() => _repo.LeerPotenciar(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(_potenciar.ToList()));
            A.CallTo(() => _repo.LeerAfinidad(A<string>._)).ReturnsLazily(() => Task.FromResult(_afinidades.ToList()));
            A.CallTo(() => _repo.LeerClientesPerfil(A<string>._, A<string>._))
                .ReturnsLazily((string g, string s) => Task.FromResult(g == "COS" && s == "CRE" ? new Dictionary<string, int>(_clientesPerfil) : new Dictionary<string, int>()));
            A.CallTo(() => _repo.LeerFichas(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily((string e, IEnumerable<string> productos) =>
                Task.FromResult(productos.Distinct(StringComparer.OrdinalIgnoreCase).Where(p => _fichas.ContainsKey(p))
                    .ToDictionary(p => p, p => _fichas[p], StringComparer.OrdinalIgnoreCase)));
            A.CallTo(() => _repo.LeerVendibles(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily((string e, IEnumerable<string> productos) =>
                Task.FromResult(new HashSet<string>(productos.Where(p => !_sinStock.Contains(p)), StringComparer.OrdinalIgnoreCase)));
            A.CallTo(() => _repo.LeerDatosGrupoVendedor(A<string>._, A<string>._, A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(_datosVendedor));
            A.CallTo(() => _repo.MarcarEntradaEnPedido(A<int>._, A<bool>._, A<int?>._)).ReturnsLazily((int id, bool entro, int? cantidad) =>
            {
                SugerenciaMostrada m = _mostradas.Single(x => x.Id == id);
                m.EntroEnPedido = entro;
                m.CantidadEnPedido = cantidad;
                return Task.CompletedTask;
            });
            A.CallTo(() => _repo.EnlazarPedido(A<Guid>._, A<int>._)).ReturnsLazily((Guid id, int pedido) =>
            {
                _sesiones[id].Pedido = pedido;
                return Task.CompletedTask;
            });

            _servicio = new ServicioSugerenciasProductos(_repo, () => HOY, ex => _errores.Add(ex));
        }

        // ---------------- Grupo del vendedor ----------------

        [TestMethod]
        public void GrupoVendedor_VendedorConEstadoPeluqueria_VePeluqueria()
        {
            var datos = new DatosGrupoVendedor { EstadoVendedor = Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA, VendedorEsteticaCliente = "PEL" };
            Assert.AreEqual(FiltroGrupoVendedor.Peluqueria, MotorSugerenciasProductos.GrupoVendedor("PEL", datos));
        }

        [TestMethod]
        public void GrupoVendedor_ElDePeluqueriaDelCliente_VePeluqueria()
        {
            Assert.AreEqual(FiltroGrupoVendedor.Peluqueria, MotorSugerenciasProductos.GrupoVendedor("PEL", _datosVendedor));
        }

        [TestMethod]
        public void GrupoVendedor_ElDeEsteticaDelCliente_VeEstetica()
        {
            Assert.AreEqual(FiltroGrupoVendedor.Estetica, MotorSugerenciasProductos.GrupoVendedor("SAN", _datosVendedor));
        }

        [TestMethod]
        public void GrupoVendedor_LlevaLasDosCosasDelCliente_VeTodo()
        {
            var datos = new DatosGrupoVendedor { EstadoVendedor = 0, VendedorEsteticaCliente = "SAN", VendedorPeluqueriaCliente = "SAN" };
            Assert.AreEqual(FiltroGrupoVendedor.Todos, MotorSugerenciasProductos.GrupoVendedor("SAN", datos));
        }

        [TestMethod]
        public void GrupoVendedor_SinVendedorOGeneral_VeTodo()
        {
            Assert.AreEqual(FiltroGrupoVendedor.Todos, MotorSugerenciasProductos.GrupoVendedor(null, _datosVendedor));
            Assert.AreEqual(FiltroGrupoVendedor.Todos, MotorSugerenciasProductos.GrupoVendedor(Constantes.Vendedores.VENDEDOR_GENERAL, _datosVendedor));
        }

        [TestMethod]
        public void GrupoVendedor_VendedorDeOtroCliente_EsEsteticaComoEnComisiones()
        {
            Assert.AreEqual(FiltroGrupoVendedor.Estetica, MotorSugerenciasProductos.GrupoVendedor("OTR", _datosVendedor));
        }

        // ---------------- Filtros ----------------

        [TestMethod]
        public void MotivoDescarte_CadaFiltro()
        {
            var carrito = Conjunto("CARRITO");
            var pendientes = Conjunto("PENDIENTE");
            var rechazados = Conjunto("RECHAZADO");
            string Motivo(FichaProductoSugerencia f, FiltroGrupoVendedor filtro = FiltroGrupoVendedor.Estetica)
                => MotorSugerenciasProductos.MotivoDescarte(f?.Producto ?? "X", f, filtro, carrito, pendientes, rechazados);

            Assert.IsNull(Motivo(NuevaFicha("BUENO", "COS", "CRE")));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_SIN_FICHA, Motivo(null));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_NO_VENDIBLE, Motivo(NuevaFicha("BAJA", "COS", "CRE", estado: -1)));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_NO_VENDIBLE, Motivo(NuevaFicha("FICT", "COS", "CRE", ficticio: true)));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_NO_SUGERIBLE, Motivo(NuevaFicha("MTP", Constantes.Productos.GRUPO_MATERIAS_PRIMAS, "X")));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_NO_SUGERIBLE, Motivo(NuevaFicha("MUESTRA", "COS", Constantes.Productos.SUBGRUPO_MUESTRAS)));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_OTRO_GRUPO, Motivo(NuevaFicha("PELU", "PEL", "CHA")));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_OTRO_GRUPO, Motivo(NuevaFicha("COSM", "COS", "CRE"), FiltroGrupoVendedor.Peluqueria));
            Assert.IsNull(Motivo(NuevaFicha("PELU", "PEL", "CHA"), FiltroGrupoVendedor.Todos));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_EN_CARRITO, Motivo(NuevaFicha("CARRITO", "COS", "CRE")));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_PENDIENTE, Motivo(NuevaFicha("PENDIENTE", "COS", "CRE")));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_RECHAZADO, Motivo(NuevaFicha("RECHAZADO", "COS", "CRE")));
        }

        // ---------------- Fuentes ----------------

        [TestMethod]
        public void CompradoJunto_CogeElMejorParYNoSugiereLoDelCarrito()
        {
            var pares = new List<ParCompradoJunto>
            {
                Par("A", "C", 0.2m), Par("B", "C", 0.5m), Par("A", "B", 0.9m), Par("Z", "D", 0.9m)
            };
            var fichas = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase) { { "B", NuevaFicha("B", "COS", "CRE", nombre: "Crema B") } };

            List<CandidatoSugerencia> candidatos = MotorSugerenciasProductos.CompradoJunto(pares, Conjunto("A", "B"), fichas);

            Assert.AreEqual(1, candidatos.Count, "B está en el carrito y Z no");
            Assert.AreEqual("C", candidatos[0].Producto);
            Assert.AreEqual(0.5m, candidatos[0].Puntuacion);
            Assert.AreEqual("Va en el 50 % de las compras de Crema B.", candidatos[0].Texto);
        }

        [TestMethod]
        public void LeToca_SoloConRitmoYPasadoDeLaMediana_GanaElQueMasSePasa()
        {
            var compras = new List<CompraDiaReposicion>();
            compras.AddRange(Dias("TOCA", -150, -120, -90, -60));       // mediana 30, hace 60 → 2
            compras.AddRange(Dias("TOCAMENOS", -70, -50, -30));         // mediana 20, hace 30 → 1,5
            compras.AddRange(Dias("NOTOCA", -90, -60, -30, -10));       // mediana 30, hace 10
            compras.AddRange(Dias("DOS", -200, -100));                  // solo 2 compras
            compras.AddRange(Dias("DEJADO", -400, -390, -380));         // mediana 10, hace 380: abandonado

            List<CandidatoSugerencia> candidatos = MotorSugerenciasProductos.LeToca(compras, HOY);

            CollectionAssert.AreEqual(new[] { "TOCA", "TOCAMENOS" }, candidatos.Select(c => c.Producto).ToArray());
            Assert.AreEqual("Lo compra cada ~30 días; el último, hace 60.", candidatos[0].Texto);
            Assert.AreEqual(MotivosSugerencia.LE_TOCA, candidatos[0].Motivo);
        }

        [TestMethod]
        public void Potenciar_LoQueNoHaCompradoYCompranClientesParecidos()
        {
            var candidatos = new List<CandidatoPotenciar>
            {
                new CandidatoPotenciar { Producto = "OFERTA", Tipo = CandidatoPotenciar.TIPO_OFERTA, Detalle = "6+2" },
                new CandidatoPotenciar { Producto = "OFERTA", Tipo = CandidatoPotenciar.TIPO_NOVEDAD },
                new CandidatoPotenciar { Producto = "NOVEDAD", Tipo = CandidatoPotenciar.TIPO_NOVEDAD },
                new CandidatoPotenciar { Producto = "YACOMPRADO", Tipo = CandidatoPotenciar.TIPO_POTENCIAR },
                new CandidatoPotenciar { Producto = "NADIE", Tipo = CandidatoPotenciar.TIPO_POTENCIAR }
            };
            var perfil = new Dictionary<string, int> { { "OFERTA", 3 }, { "NOVEDAD", 1 }, { "YACOMPRADO", 50 } };

            List<CandidatoSugerencia> resultado = MotorSugerenciasProductos.Potenciar(candidatos, perfil, Conjunto("YACOMPRADO"));

            CollectionAssert.AreEqual(new[] { "OFERTA", "NOVEDAD" }, resultado.Select(c => c.Producto).ToArray());
            Assert.AreEqual("En oferta (6+2); lo compran 3 clientes parecidos.", resultado[0].Texto);
            Assert.AreEqual("Novedad; lo compra 1 cliente parecido.", resultado[1].Texto);
        }

        [TestMethod]
        public void Perfil_EsElGrupoYSubgrupoDondeMasCompra()
        {
            var fichas = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase)
            {
                { "A", NuevaFicha("A", "COS", "CRE") }, { "B", NuevaFicha("B", "PEL", "CHA") }, { "C", NuevaFicha("C", "PEL", "CHA") }
            };
            var compras = new List<CompraDiaReposicion>
            {
                new CompraDiaReposicion { Producto = "A", BaseImponible = 100 },
                new CompraDiaReposicion { Producto = "B", BaseImponible = 60 },
                new CompraDiaReposicion { Producto = "C", BaseImponible = 60 }
            };

            Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(compras, fichas);

            Assert.AreEqual("PEL", perfil.Item1);
            Assert.AreEqual("CHA", perfil.Item2);
        }

        // ---------------- Mezcla ----------------

        [TestMethod]
        public void Elegir_UnaDeCadaFuenteYDespuesPorTurnos()
        {
            var candidatos = new List<CandidatoSugerencia>
            {
                Candidato("J1", MotivosSugerencia.COMPRADO_JUNTO, 0.9m), Candidato("J2", MotivosSugerencia.COMPRADO_JUNTO, 0.8m),
                Candidato("J3", MotivosSugerencia.COMPRADO_JUNTO, 0.7m), Candidato("T1", MotivosSugerencia.LE_TOCA, 2m),
                Candidato("P1", MotivosSugerencia.POTENCIAR, 10m)
            };

            CollectionAssert.AreEqual(new[] { "J1", "T1", "P1" },
                MotorSugerenciasProductos.Elegir(candidatos, null, null, 3).Select(c => c.Producto).ToArray());
            CollectionAssert.AreEqual(new[] { "J1", "T1", "P1", "J2" },
                MotorSugerenciasProductos.Elegir(candidatos, null, null, 4).Select(c => c.Producto).ToArray());
            // Con una de «le toca» ya a la vista y J1 ya enseñada, lo siguiente: J2 y P1
            CollectionAssert.AreEqual(new[] { "J2", "P1" },
                MotorSugerenciasProductos.Elegir(candidatos, new[] { MotivosSugerencia.LE_TOCA }, Conjunto("J1"), 2).Select(c => c.Producto).ToArray());
            // Sin «le toca» ni «potenciar», se llena con lo que hay
            CollectionAssert.AreEqual(new[] { "J1", "J2", "J3" },
                MotorSugerenciasProductos.Elegir(candidatos.Where(c => c.Motivo == MotivosSugerencia.COMPRADO_JUNTO), null, null, 3).Select(c => c.Producto).ToArray());
        }

        [TestMethod]
        public void HuecoLibre_ElPrimeroQueNoEstaOcupado()
        {
            Assert.AreEqual(1, MotorSugerenciasProductos.HuecoLibre(new int[0]));
            Assert.AreEqual(2, MotorSugerenciasProductos.HuecoLibre(new[] { 1, 3 }));
            Assert.AreEqual(0, MotorSugerenciasProductos.HuecoLibre(new[] { 1, 2, 3, 4 }));
        }

        // ---------------- Job: pares y perfiles ----------------

        [TestMethod]
        public void Pares_SoporteConfianzaYLift()
        {
            var fichas = FichasCos("A", "B", "C");
            var compras = new List<CompraDiaReposicion>();
            // 10 cestas: A y B juntos en 6; A solo en 2; C solo en 2
            for (int i = 0; i < 6; i++)
            {
                compras.AddRange(Cesta("CLI" + i, -i - 1, "A", "B"));
            }
            compras.AddRange(Cesta("CLI6", -1, "A"));
            compras.AddRange(Cesta("CLI7", -1, "A"));
            compras.AddRange(Cesta("CLI8", -1, "C"));
            compras.AddRange(Cesta("CLI9", -1, "C"));

            List<ParCompradoJunto> pares = CalculadoraParesCompradosJuntos.Pares(compras, fichas);

            ParCompradoJunto ab = pares.Single(p => p.ProductoA == "A" && p.ProductoB == "B");
            Assert.AreEqual(6, ab.VecesJuntos);
            Assert.AreEqual(0.6m, ab.Soporte);
            Assert.AreEqual(0.75m, ab.Confianza);            // 6 de las 8 cestas con A
            Assert.AreEqual(1.25m, ab.Lift);                 // 0,75 ÷ (6 ÷ 10)
            ParCompradoJunto ba = pares.Single(p => p.ProductoA == "B" && p.ProductoB == "A");
            Assert.AreEqual(1m, ba.Confianza);               // siempre que va B va A
            Assert.IsFalse(pares.Any(p => p.ProductoA == "C" || p.ProductoB == "C"));
        }

        [TestMethod]
        public void Pares_SinVecesSuficientesONoSugeribleOCestaEnorme_NoCuentan()
        {
            var fichas = FichasCos("A", "B", "M");
            fichas["M"].Ficticio = true;
            var compras = new List<CompraDiaReposicion>();
            for (int i = 0; i < CalculadoraParesCompradosJuntos.MINIMO_VECES_JUNTOS - 1; i++)
            {
                compras.AddRange(Cesta("CLI" + i, -1, "A", "B", "M"));
            }
            compras.AddRange(Cesta("OTRO", -1, "B"));
            // Una cesta gigante con A y B no suma la quinta vez
            string[] gigante = new[] { "A", "B" }.Concat(Enumerable.Range(0, CalculadoraParesCompradosJuntos.MAXIMO_PRODUCTOS_CESTA).Select(n => "G" + n)).ToArray();
            foreach (string g in gigante.Where(g => g.StartsWith("G")))
            {
                fichas[g] = NuevaFicha(g, "COS", "CRE");
            }
            compras.AddRange(Cesta("TIENDA", -2, gigante));

            Assert.AreEqual(0, CalculadoraParesCompradosJuntos.Pares(compras, fichas).Count);
        }

        [TestMethod]
        public void PerfilesProductos_CuentaClientesDelPerfilEnDoceMeses()
        {
            var fichas = FichasCos("A", "B", "X");
            fichas["X"].SubGrupo = "OTR";
            var compras = new List<CompraDiaReposicion>();
            compras.AddRange(Cesta("C1", -10, "A", "B"));
            compras.AddRange(Cesta("C2", -20, "A", "B"));
            compras.AddRange(Cesta("C3", -400, "A", "B"));   // perfil COS|CRE, pero la compra es de hace más de 12 meses
            compras.AddRange(Cesta("C3", -5, "X"));          // ... y X pesa menos que A + B
            compras.AddRange(Cesta("C4", -5, "A"));

            List<ProductoPerfil> perfiles = CalculadoraParesCompradosJuntos.PerfilesProductos(compras, fichas, HOY);

            Assert.AreEqual(3, perfiles.Single(p => p.Producto == "A").Clientes);   // C1, C2 y C4
            Assert.AreEqual(2, perfiles.Single(p => p.Producto == "B").Clientes);
            Assert.IsFalse(perfiles.Any(p => p.Producto == "X"), "Solo un cliente: no llega al mínimo");
            Assert.IsTrue(perfiles.All(p => p.Grupo == "COS" && p.SubGrupo == "CRE"));
        }

        [TestMethod]
        public void Job_InterruptorApagado_NoHaceNada()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Apagado", () => HOY).Ejecutar();

            Assert.AreEqual(SugerenciasProductosJobsService.MOTIVO_APAGADO, r.MotivoNoCalcula);
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => repo.Guardar(A<List<ParCompradoJunto>>._, A<List<ProductoPerfil>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Job_SinTablas_NoHaceNada()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(false);
            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.AreEqual(SugerenciasProductosJobsService.MOTIVO_SIN_TABLAS, r.MotivoNoCalcula);
            A.CallTo(() => repo.Guardar(A<List<ParCompradoJunto>>._, A<List<ProductoPerfil>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Job_Activo_LeeVeinticuatroMesesYGuarda()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            var compras = new List<CompraDiaReposicion>();
            for (int i = 0; i < 6; i++)
            {
                compras.AddRange(Cesta("CLI" + i, -i - 1, "A", "B"));
            }
            compras.AddRange(Cesta("SOLO", -1, "C"));
            compras.AddRange(Cesta("SOLO2", -2, "C"));
            A.CallTo(() => repo.LeerCompras("1", HOY.Date.AddMonths(-24), HOY.Date)).Returns(compras);
            A.CallTo(() => repo.LeerFichas("1")).Returns(FichasCos("A", "B", "C"));
            List<ParCompradoJunto> guardados = null;
            A.CallTo(() => repo.Guardar(A<List<ParCompradoJunto>>._, A<List<ProductoPerfil>>._, A<DateTime>._))
                .Invokes((List<ParCompradoJunto> p, List<ProductoPerfil> pp, DateTime f) => guardados = p);

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.IsNull(r.MotivoNoCalcula);
            Assert.AreEqual(2, r.Pares);
            Assert.AreEqual(2, guardados.Count);
        }

        [TestMethod]
        public void Job_SinCompras_NoPisaLoQueHabia()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new List<CompraDiaReposicion>());

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.AreEqual(SugerenciasProductosJobsService.MOTIVO_SIN_COMPRAS, r.MotivoNoCalcula);
            A.CallTo(() => repo.Guardar(A<List<ParCompradoJunto>>._, A<List<ProductoPerfil>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        // ---------------- Servicio ----------------

        [TestMethod]
        public async Task Calcular_SinTablas_DevuelveListaVaciaYNoEscribe()
        {
            _existenTablas = false;
            Guid id = Guid.NewGuid();

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(id, "CARRO1"), "carlos");

            Assert.AreEqual(id, r.IdSesion);
            Assert.AreEqual(0, r.Sugerencias.Count);
            A.CallTo(() => _repo.CrearSesion(A<SesionSugerencias>._)).MustNotHaveHappened();
            A.CallTo(() => _repo.InsertarMostrada(A<SugerenciaMostrada>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Calcular_UnaDeCadaFuenteDelGrupoDelVendedor_YLasRegistra()
        {
            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1"), "NUEVAVISION\\carlos");

            Assert.AreNotEqual(Guid.Empty, r.IdSesion);
            Assert.AreEqual(MotorSugerenciasProductos.SUGERENCIAS_VISIBLES, r.Sugerencias.Count);
            CollectionAssert.AreEqual(new[] { "JUNTO1", "LETOCA1", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray(),
                "PELU1 tiene más confianza, pero es de peluquería y el vendedor es el de estética");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, r.Sugerencias.Select(s => s.Posicion).ToArray());
            CollectionAssert.AreEqual(new[] { MotivosSugerencia.COMPRADO_JUNTO, MotivosSugerencia.LE_TOCA, MotivosSugerencia.POTENCIAR },
                r.Sugerencias.Select(s => s.Motivo).ToArray());
            Assert.AreEqual("Lo compra cada ~30 días; el último, hace 60.", r.Sugerencias[1].Texto);
            Assert.AreEqual("Nombre de JUNTO1", r.Sugerencias[0].Nombre);
            Assert.AreEqual(3, _mostradas.Count);
            Assert.IsTrue(_mostradas.All(m => m.Respuesta == null && m.VersionMotor == MotorSugerenciasProductos.VERSION_MOTOR));
            Assert.AreEqual("NUEVAVISION\\carlos", _sesiones[r.IdSesion].Usuario);
            Assert.AreEqual("CARRO1:1", _sesiones[r.IdSesion].Carrito);
        }

        [TestMethod]
        public async Task Calcular_VendedorDePeluqueria_SoloPeluqueria()
        {
            SugerenciasProductosDTO r = await _servicio.Calcular(PeticionDeVendedor(null, "CARRO1", "PEL"), "carlos");

            CollectionAssert.AreEqual(new[] { "PELU1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Calcular_SinStockNiLlegada_NoSeSugiere()
        {
            _sinStock.Add("JUNTO1");
            _sinStock.Add("LETOCA1");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "JUNTO2", "LETOCA2", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Calcular_RechazadoEnSesentaDiasOPendiente_NoSeSugiere()
        {
            _rechazados.Add("JUNTO1");
            _pendientes.Add("POT1");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "JUNTO2", "LETOCA1", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Calcular_OtraVezConLasMismas_SeMantienenSinBailar()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            // Aparece un par mejor: no debe desplazar a las que ya se ven
            _pares.Add(Par("CARRO1", "JUNTO2", 0.95m));

            SugerenciasProductosDTO segunda = await _servicio.Calcular(Peticion(primera.IdSesion, "CARRO1"), "carlos");

            Assert.AreEqual(primera.IdSesion, segunda.IdSesion);
            CollectionAssert.AreEqual(primera.Sugerencias.Select(s => s.IdSugerencia).ToArray(), segunda.Sugerencias.Select(s => s.IdSugerencia).ToArray());
            Assert.AreEqual(3, _mostradas.Count, "No se registra nada nuevo");
            A.CallTo(() => _repo.CrearSesion(A<SesionSugerencias>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Calcular_ElProductoSugeridoEntraEnElCarrito_SeSustituyeEnSuHueco()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            SugerenciasProductosDTO segunda = await _servicio.Calcular(Peticion(primera.IdSesion, "CARRO1", "LETOCA1"), "carlos");

            CollectionAssert.AreEqual(new[] { "JUNTO1", "LETOCA2", "POT1" }, segunda.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.AreEqual(2, segunda.Sugerencias.Single(s => s.Producto == "LETOCA2").Posicion);
            Assert.IsNull(_mostradas.Single(m => m.Producto == "LETOCA1").Respuesta, "Sin tocar: no cuenta como ofrecida");
        }

        [TestMethod]
        public async Task Calcular_SesionDeOtroCliente_EmpiezaSesionNueva()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            CalcularSugerenciasDTO peticion = Peticion(primera.IdSesion, "CARRO1");
            peticion.Cliente = "2002";

            SugerenciasProductosDTO segunda = await _servicio.Calcular(peticion, "carlos");

            Assert.AreNotEqual(primera.IdSesion, segunda.IdSesion);
            Assert.AreEqual("2002", _sesiones[segunda.IdSesion].Cliente);
        }

        [TestMethod]
        public async Task Calcular_AplicacionQueNoVale_Lanza400()
        {
            CalcularSugerenciasDTO peticion = Peticion(null, "CARRO1");
            peticion.Aplicacion = "TiendaOnline";

            SugerenciasProductosException ex = await Assert.ThrowsExceptionAsync<SugerenciasProductosException>(() => _servicio.Calcular(peticion, "carlos"));
            Assert.IsFalse(ex.NoEncontrada);
        }

        [TestMethod]
        public async Task Responder_NoLeInteresa_DevuelveLaSustitutaEnSuHuecoYDeLaMismaFuente()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            SugerenciaProductoDTO junto1 = primera.Sugerencias.Single(s => s.Producto == "JUNTO1");

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(junto1.IdSugerencia,
                new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.NO_LE_INTERESA });

            Assert.AreEqual(RespuestasSugerencia.NO_LE_INTERESA, _mostradas.Single(m => m.Id == junto1.IdSugerencia).Respuesta);
            Assert.IsNotNull(r.Sustituta);
            Assert.AreEqual("JUNTO2", r.Sustituta.Producto);
            Assert.AreEqual(junto1.Posicion, r.Sustituta.Posicion);
            CollectionAssert.AreEqual(new[] { "JUNTO2", "LETOCA1", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Responder_NoLeInteresaSinMasCandidatos_SustitutaDeOtraFuenteONula()
        {
            _pares.RemoveAll(p => p.ProductoB == "JUNTO2");
            _potenciar.Clear();
            _compras.RemoveAll(c => c.Producto == "LETOCA2");
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            Assert.AreEqual(2, primera.Sugerencias.Count);

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(primera.Sugerencias[0].IdSugerencia,
                new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.NO_LE_INTERESA });

            Assert.IsNull(r.Sustituta);
            CollectionAssert.AreEqual(new[] { "LETOCA1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Responder_Anadido_SoloSeApunta()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(primera.Sugerencias[1].IdSugerencia,
                new RespuestaSugerenciaDTO { Respuesta = "anadido" });

            Assert.AreEqual(RespuestasSugerencia.ANADIDO, r.Respuesta);
            Assert.IsNull(r.Sustituta);
            Assert.AreEqual(RespuestasSugerencia.ANADIDO, _mostradas.Single(m => m.Id == primera.Sugerencias[1].IdSugerencia).Respuesta);
            Assert.AreEqual(3, _mostradas.Count);
        }

        [TestMethod]
        public async Task Responder_SugerenciaQueNoExisteORespuestaQueNoVale()
        {
            SugerenciasProductosException noExiste = await Assert.ThrowsExceptionAsync<SugerenciasProductosException>(
                () => _servicio.Responder(999, new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.ANADIDO }));
            Assert.IsTrue(noExiste.NoEncontrada);
            SugerenciasProductosException noVale = await Assert.ThrowsExceptionAsync<SugerenciasProductosException>(
                () => _servicio.Responder(1, new RespuestaSugerenciaDTO { Respuesta = "Quizá" }));
            Assert.IsFalse(noVale.NoEncontrada);
        }

        [TestMethod]
        public async Task Responder_SinTablas_404()
        {
            _existenTablas = false;
            SugerenciasProductosException ex = await Assert.ThrowsExceptionAsync<SugerenciasProductosException>(
                () => _servicio.Responder(1, new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.ANADIDO }));
            Assert.IsTrue(ex.NoEncontrada);
        }

        [TestMethod]
        public async Task EnlazarPedido_MarcaLasQueEntraronYConCuantasUnidades()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            var pedido = new PedidoVentaDTO
            {
                numero = 925137,
                IdSesionSugerencias = primera.IdSesion,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "CARRO1", Cantidad = 1, tipoLinea = 1 },
                    new LineaPedidoVentaDTO { Producto = "JUNTO1 ", Cantidad = 2, tipoLinea = 1 },
                    new LineaPedidoVentaDTO { Producto = "JUNTO1", Cantidad = 1, tipoLinea = 1 },
                    new LineaPedidoVentaDTO { Producto = "POT1", Cantidad = 5, tipoLinea = 2 }   // cuenta contable: no cuenta
                }
            };

            await _servicio.EnlazarPedido(pedido);

            Assert.AreEqual(925137, _sesiones[primera.IdSesion].Pedido);
            SugerenciaMostrada junto1 = _mostradas.Single(m => m.Producto == "JUNTO1");
            Assert.AreEqual(true, junto1.EntroEnPedido);
            Assert.AreEqual(3, junto1.CantidadEnPedido);
            Assert.AreEqual(false, _mostradas.Single(m => m.Producto == "POT1").EntroEnPedido);
            Assert.IsNull(_mostradas.Single(m => m.Producto == "POT1").CantidadEnPedido);
        }

        [TestMethod]
        public async Task EnlazarPedido_SinSesion_NoHaceNada()
        {
            await _servicio.EnlazarPedido(new PedidoVentaDTO { numero = 1 });

            A.CallTo(() => _repo.ExistenTablas()).MustNotHaveHappened();
            A.CallTo(() => _repo.EnlazarPedido(A<Guid>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnlazarPedido_FallaLaBd_NoRompeElGuardadoYVaAElmah()
        {
            A.CallTo(() => _repo.ExistenTablas()).Throws(new InvalidOperationException("BD caída"));

            await _servicio.EnlazarPedido(new PedidoVentaDTO { numero = 1, IdSesionSugerencias = Guid.NewGuid() });

            Assert.AreEqual(1, _errores.Count);
            StringAssert.Contains(_errores[0].Message, "#670");
        }

        [TestMethod]
        public async Task EnlazarPedidoSinFallar_SinSesion_NoAbreConexion()
        {
            // Sin IdSesionSugerencias sale antes de crear el contexto: los tests de PedidosVentaController no se enteran.
            await ServicioSugerenciasProductos.EnlazarPedidoSinFallar(new PedidoVentaDTO { numero = 1 });
            await ServicioSugerenciasProductos.EnlazarPedidoSinFallar(null);
        }

        // ---------------- Unir pedidos (ampliar un pedido pendiente desde la plantilla) ----------------

        [TestMethod]
        public async Task EnlazarUnion_ConSesionEnLaAmpliacion_EnlazaElPedidoUnidoYMarcaLasQueEntraron()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            var ampliacion = new PedidoVentaDTO
            {
                IdSesionSugerencias = primera.IdSesion,
                Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { Producto = "JUNTO1", Cantidad = 2, tipoLinea = 1 } }
            };
            var unido = new PedidoVentaDTO
            {
                numero = 925140,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { Producto = "CARRO1", Cantidad = 1, tipoLinea = 1 },
                    new LineaPedidoVentaDTO { Producto = "JUNTO1", Cantidad = 2, tipoLinea = 1 }
                }
            };

            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, ampliacion, p => _servicio.EnlazarPedido(p));

            Assert.AreEqual(primera.IdSesion, unido.IdSesionSugerencias);
            Assert.AreEqual(925140, _sesiones[primera.IdSesion].Pedido, "La sesión queda con el pedido que resulta de la unión");
            SugerenciaMostrada junto1 = _mostradas.Single(m => m.Producto == "JUNTO1");
            Assert.AreEqual(true, junto1.EntroEnPedido);
            Assert.AreEqual(2, junto1.CantidadEnPedido);
        }

        [TestMethod]
        public async Task EnlazarUnion_SinSesion_NoHaceNada()
        {
            int llamadas = 0;
            var unido = new PedidoVentaDTO { numero = 925140 };

            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, new PedidoVentaDTO(), p => { llamadas++; return Task.CompletedTask; });
            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, new PedidoVentaDTO { IdSesionSugerencias = Guid.Empty }, p => { llamadas++; return Task.CompletedTask; });
            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, null, p => { llamadas++; return Task.CompletedTask; });
            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(null, new PedidoVentaDTO { IdSesionSugerencias = Guid.NewGuid() }, p => { llamadas++; return Task.CompletedTask; });
            // Sin sesión sale antes de abrir conexión (el camino real que usa UnirPedidos con la unión de dos números)
            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, null);

            Assert.AreEqual(0, llamadas);
            Assert.IsNull(unido.IdSesionSugerencias);
        }

        [TestMethod]
        public async Task EnlazarUnion_FallaElEnlace_NoRompeLaUnionYVaAElmah()
        {
            var errores = new List<Exception>();
            var unido = new PedidoVentaDTO { numero = 925140 };

            await ServicioSugerenciasProductos.EnlazarUnionSinFallar(unido, new PedidoVentaDTO { IdSesionSugerencias = Guid.NewGuid() },
                p => throw new InvalidOperationException("BD caída"), errores.Add);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "#670");
            StringAssert.Contains(errores[0].Message, "925140");
        }

        [TestMethod]
        public void Carrito_SeGuardaYSeLeeIgual()
        {
            var carrito = new Dictionary<string, int> { { "B", 2 }, { "A", 1 } };
            string texto = SugerenciasProductosSql.TextoCarrito(carrito);

            Assert.AreEqual("A:1;B:2", texto);
            CollectionAssert.AreEquivalent(carrito.ToList(), SugerenciasProductosSql.LeerCarrito(texto).ToList());
        }

        // ---------------- Controlador ----------------

        [TestMethod]
        public async Task Controlador_PeticionQueNoVale_400YNoEncontrada_404()
        {
            var servicio = A.Fake<IServicioSugerenciasProductos>();
            A.CallTo(() => servicio.Calcular(A<CalcularSugerenciasDTO>._, A<string>._)).Throws(new SugerenciasProductosException(false, "Falta el cliente."));
            A.CallTo(() => servicio.Responder(A<int>._, A<RespuestaSugerenciaDTO>._)).Throws(new SugerenciasProductosException(true, "No existe."));
            var controlador = new SugerenciasProductosController(() => servicio);

            Assert.IsInstanceOfType(await controlador.PostCalcular(new CalcularSugerenciasDTO()), typeof(BadRequestErrorMessageResult));
            var noEncontrada = await controlador.PostRespuesta(5, new RespuestaSugerenciaDTO()) as NegotiatedContentResult<string>;
            Assert.IsNotNull(noEncontrada);
            Assert.AreEqual(HttpStatusCode.NotFound, noEncontrada.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Calcular_200ConLasSugerencias()
        {
            var servicio = A.Fake<IServicioSugerenciasProductos>();
            var dto = new SugerenciasProductosDTO { IdSesion = Guid.NewGuid() };
            A.CallTo(() => servicio.Calcular(A<CalcularSugerenciasDTO>._, A<string>._)).Returns(dto);
            var controlador = new SugerenciasProductosController(() => servicio);

            var ok = await controlador.PostCalcular(new CalcularSugerenciasDTO()) as OkNegotiatedContentResult<SugerenciasProductosDTO>;

            Assert.AreSame(dto, ok?.Content);
        }

        // ---------------- NestoAPI#671 (corte 2a): «Parecidos», popularidad y registro ----------------

        [TestMethod]
        public void Afinidades_CosenoConLoQueNoHaComprado_YDeterminista()
        {
            var compras = new List<CompraDiaReposicion>();
            foreach (string c in new[] { "C1", "C2", "C3" })
            {
                compras.AddRange(Cesta(c, -10, "A", "B", "C"));
            }
            compras.AddRange(Cesta("C4", -10, "A", "B"));
            compras.AddRange(Cesta("C5", -10, "A"));
            Dictionary<string, FichaProductoSugerencia> fichas = FichasCos("A", "B", "C");

            List<AfinidadCliente> afinidades = CalculadoraAfinidadProductos.Afinidades(compras, fichas);

            // sim(A,B) = 4/√(5·4) = 0,894; sim(A,C) = 3/√(5·3) = 0,775; sim(B,C) = 3/√(4·3) = 0,866
            List<AfinidadCliente> c5 = afinidades.Where(a => a.Cliente == "C5").OrderBy(a => a.Rango).ToList();
            CollectionAssert.AreEqual(new[] { "B", "C" }, c5.Select(a => a.Producto).ToArray());
            Assert.AreEqual(0.894427m, c5[0].Afinidad);
            Assert.AreEqual(0.774597m, c5[1].Afinidad);
            AfinidadCliente c4 = afinidades.Single(a => a.Cliente == "C4");
            Assert.AreEqual("C", c4.Producto, "Lo único que no ha comprado");
            Assert.AreEqual(0.774597m + 0.866025m, c4.Afinidad);
            Assert.IsFalse(afinidades.Any(a => a.Cliente == "C1"), "Ya lo compra todo");

            List<AfinidadCliente> otraVez = CalculadoraAfinidadProductos.Afinidades(Enumerable.Reverse(compras).ToList(), fichas);
            CollectionAssert.AreEqual(afinidades.Select(a => a.Cliente + a.Producto + a.Afinidad + a.Rango).ToArray(),
                otraVez.Select(a => a.Cliente + a.Producto + a.Afinidad + a.Rango).ToArray(), "El orden de las compras no cambia nada");
        }

        [TestMethod]
        public void Afinidades_PocosClientesComunesONoSugerible_NoCuentan()
        {
            var compras = new List<CompraDiaReposicion>();
            compras.AddRange(Cesta("C1", -10, "A", "B", "MUESTRA"));
            compras.AddRange(Cesta("C2", -10, "A", "B", "MUESTRA"));
            compras.AddRange(Cesta("C3", -10, "A", "MUESTRA"));
            Dictionary<string, FichaProductoSugerencia> fichas = FichasCos("A", "B");
            fichas["MUESTRA"] = NuevaFicha("MUESTRA", "COS", Constantes.Productos.SUBGRUPO_MUESTRAS);

            List<AfinidadCliente> afinidades = CalculadoraAfinidadProductos.Afinidades(compras, fichas);

            Assert.AreEqual(0, afinidades.Count, "A y B solo tienen 2 clientes comunes; la muestra no se sugiere nunca");
        }

        [TestMethod]
        public void Popularidad_ClientesEnDoceYTresMesesYCestas()
        {
            var compras = new List<CompraDiaReposicion>();
            compras.AddRange(Cesta("C1", -10, "A", "B"));
            compras.AddRange(Cesta("C1", -20, "A"));
            compras.AddRange(Cesta("C2", -200, "A"));
            compras.AddRange(Cesta("C3", -200, "B"));
            compras.AddRange(Cesta("C4", -400, "B"));    // de hace más de 12 meses: no cuenta

            List<PopularidadProducto> popularidad = CalculadoraAfinidadProductos.Popularidad(compras, FichasCos("A", "B"), HOY);

            PopularidadProducto a = popularidad.Single(p => p.Producto == "A");
            PopularidadProducto b = popularidad.Single(p => p.Producto == "B");
            Assert.AreEqual(2, a.Clientes12Meses);
            Assert.AreEqual(1, a.Clientes3Meses);
            Assert.AreEqual(3, a.Cestas12Meses);
            Assert.AreEqual(1, a.Rango, "Empatan en clientes; A tiene más cestas");
            Assert.AreEqual(2, b.Clientes12Meses);
            Assert.AreEqual(2, b.Rango);
        }

        [TestMethod]
        public void Parecidos_SinLoQueYaHaCompradoYFuentesEnOrden()
        {
            var afinidades = new List<AfinidadCliente>
            {
                new AfinidadCliente { Cliente = CLIENTE, Producto = "X", Afinidad = 0.5m, Rango = 2 },
                new AfinidadCliente { Cliente = CLIENTE, Producto = "Y", Afinidad = 0.9m, Rango = 1 },
                new AfinidadCliente { Cliente = CLIENTE, Producto = "COMPRADO", Afinidad = 2m, Rango = 0 }
            };

            List<CandidatoSugerencia> parecidos = MotorSugerenciasProductos.Parecidos(afinidades, Conjunto("COMPRADO"));

            CollectionAssert.AreEqual(new[] { "Y", "X" }, parecidos.Select(c => c.Producto).ToArray());
            Assert.IsTrue(parecidos.All(c => c.Motivo == MotivosSugerencia.PARECIDOS));
            StringAssert.StartsWith(parecidos[0].Texto, "Lo compran clientes parecidos");
            Assert.AreEqual(MotivosSugerencia.PARECIDOS, MotorSugerenciasProductos.ORDEN_FUENTES.Last(), "Va la última: sin afinidad, todo como en el corte 1");

            Dictionary<string, string> fuentes = MotorSugerenciasProductos.Fuentes(new[]
            {
                Candidato("Y", MotivosSugerencia.PARECIDOS, 1), Candidato("Y", MotivosSugerencia.COMPRADO_JUNTO, 1), Candidato("Z", MotivosSugerencia.LE_TOCA, 1)
            });
            Assert.AreEqual("CompradoJunto,Parecidos", fuentes["Y"]);
            Assert.AreEqual("LeToca", fuentes["Z"]);
        }

        [TestMethod]
        public void Job_ConTablasDelModelo_GuardaAfinidadYPopularidadEnLaMismaPasada()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            A.CallTo(() => repo.ExistenTablasModelo()).Returns(true);
            var compras = new List<CompraDiaReposicion>();
            foreach (string c in new[] { "C1", "C2", "C3" })
            {
                compras.AddRange(Cesta(c, -10, "A", "B", "C"));
            }
            compras.AddRange(Cesta("C4", -10, "A", "B"));
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(compras);
            A.CallTo(() => repo.LeerFichas("1")).Returns(FichasCos("A", "B", "C"));
            List<AfinidadCliente> guardadas = null;
            A.CallTo(() => repo.GuardarModelo(A<List<AfinidadCliente>>._, A<List<PopularidadProducto>>._, A<DateTime>._))
                .Invokes((List<AfinidadCliente> a, List<PopularidadProducto> p, DateTime f) => guardadas = a);

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.Guardar(A<List<ParCompradoJunto>>._, A<List<ProductoPerfil>>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, r.Afinidades);
            Assert.AreEqual("C4", guardadas.Single().Cliente);
            Assert.AreEqual(3, r.Popularidad);
        }

        [TestMethod]
        public void Job_SinTablasDelModelo_SoloParesYPerfiles()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            A.CallTo(() => repo.ExistenTablasModelo()).Returns(false);
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Cesta("C1", -1, "A").ToList());
            A.CallTo(() => repo.LeerFichas("1")).Returns(FichasCos("A"));

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.IsNull(r.MotivoNoCalcula);
            A.CallTo(() => repo.GuardarModelo(A<List<AfinidadCliente>>._, A<List<PopularidadProducto>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Calcular_ConAfinidad_LasTresFuentesDeAntesNoCambianYSeRegistranFuentesYCarrito()
        {
            Ficha("PARECIDO1", "COS", "CRE");
            _afinidades.Add(new AfinidadCliente { Cliente = CLIENTE, Producto = "PARECIDO1", Afinidad = 3m, Rango = 1 });
            _afinidades.Add(new AfinidadCliente { Cliente = CLIENTE, Producto = "JUNTO1", Afinidad = 1m, Rango = 2 });

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1", "OTRO"), "carlos");

            CollectionAssert.AreEqual(new[] { "JUNTO1", "LETOCA1", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray(),
                "Con 3 visibles y 4 fuentes, «Parecidos» va detrás");
            SugerenciaMostrada junto = _mostradas.Single(m => m.Producto == "JUNTO1");
            Assert.AreEqual("CompradoJunto,Parecidos", junto.Fuentes);
            Assert.AreEqual(2, junto.TamanoCarrito);
            Assert.AreEqual("LeToca", _mostradas.Single(m => m.Producto == "LETOCA1").Fuentes);
            Assert.IsTrue(_mostradas.All(m => !m.Exploracion));
        }

        [TestMethod]
        public async Task Calcular_SinOtrasFuentes_SaleParecidosConSuTexto()
        {
            Ficha("PARECIDO1", "COS", "CRE");
            _afinidades.Add(new AfinidadCliente { Cliente = CLIENTE, Producto = "PARECIDO1", Afinidad = 3m, Rango = 1 });
            _pares.Clear();
            _potenciar.Clear();
            _compras.Clear();

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            SugerenciaProductoDTO parecido = r.Sugerencias.Single();
            Assert.AreEqual("PARECIDO1", parecido.Producto);
            Assert.AreEqual(MotivosSugerencia.PARECIDOS, parecido.Motivo);
            Assert.AreEqual(MotorSugerenciasProductos.TextoParecidos(), parecido.Texto);
            Assert.AreEqual(0, _mostradas.Single().TamanoCarrito);
        }

        // ---------------- Nesto#537 (segunda vuelta): fichas más ricas ----------------

        [TestMethod]
        public async Task Calcular_FichasConFotoTamanoYFamilia_YLasFotosDeUnaVez()
        {
            _fichas["JUNTO1"].Nombre = "Crema Reafirmante";
            _fichas["JUNTO1"].Tamano = 500;
            _fichas["JUNTO1"].UnidadMedida = "ml ";
            _fichas["JUNTO1"].Familia = "LISAP";
            _fichas["JUNTO1"].NombreFamilia = "Lisap Milano";
            _fichas["LETOCA1"].Familia = "EVA";            // sin descripción: el código
            var fotos = A.Fake<IFotosProductoAlmacen>();
            var pedidas = new List<List<string>>();
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).ReturnsLazily((IEnumerable<string> productos) =>
            {
                pedidas.Add(productos.ToList());
                IDictionary<string, string> urls = new Dictionary<string, string> { { "JUNTO1", "https://tienda/junto1.jpg" }, { "LETOCA1", null } };
                return Task.FromResult(urls);
            });
            var servicio = new ServicioSugerenciasProductos(_repo, () => HOY, ex => _errores.Add(ex), fotos);

            SugerenciasProductosDTO r = await servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            Assert.AreEqual(1, pedidas.Count, "Una sola llamada con todas las que se ven, no una por sugerencia");
            CollectionAssert.AreEquivalent(new[] { "JUNTO1", "LETOCA1", "POT1" }, pedidas[0]);
            SugerenciaProductoDTO junto = r.Sugerencias.Single(s => s.Producto == "JUNTO1");
            Assert.AreEqual("https://tienda/junto1.jpg", junto.UrlFoto);
            Assert.AreEqual((short)500, junto.Tamano);
            Assert.AreEqual("ml", junto.UnidadMedida);
            Assert.AreEqual("Lisap Milano", junto.Familia);
            Assert.AreEqual("Crema Reafirmante 500 ml", junto.NombreConTamano);
            SugerenciaProductoDTO letoca = r.Sugerencias.Single(s => s.Producto == "LETOCA1");
            Assert.IsNull(letoca.UrlFoto);
            Assert.AreEqual("EVA", letoca.Familia);
            Assert.AreEqual("Nombre de LETOCA1", letoca.NombreConTamano, "Sin tamaño, el nombre solo");
            Assert.AreEqual(0, _errores.Count);
        }

        [TestMethod]
        public async Task Calcular_LasFotosFallan_LasFichasVanSinFotoYNoRompe()
        {
            var fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).Throws(new InvalidOperationException("La tienda no contesta"));
            var servicio = new ServicioSugerenciasProductos(_repo, () => HOY, ex => _errores.Add(ex), fotos);

            SugerenciasProductosDTO r = await servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count);
            Assert.IsTrue(r.Sugerencias.All(s => s.UrlFoto == null));
            Assert.AreEqual(1, _errores.Count);
            StringAssert.Contains(_errores[0].Message, "#537");
        }

        [TestMethod]
        public async Task Calcular_SinServicioDeFotos_SinFoto()
        {
            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");

            Assert.IsTrue(r.Sugerencias.All(s => s.UrlFoto == null));
            Assert.IsTrue(r.Sugerencias.All(s => s.NombreConTamano == "Nombre de " + s.Producto));
        }

        [TestMethod]
        public void NombreConTamano_ComoEnLosPedidosDeCompra()
        {
            Assert.AreEqual("Crema Reafirmante 500 ml", ServicioSugerenciasProductos.NombreConTamano(" Crema Reafirmante ", 500, " ml "));
            Assert.AreEqual("Champú 1000", ServicioSugerenciasProductos.NombreConTamano("Champú", 1000, null));
            Assert.AreEqual("Champú", ServicioSugerenciasProductos.NombreConTamano("Champú", 0, "ml"));
            Assert.AreEqual("Champú", ServicioSugerenciasProductos.NombreConTamano("Champú", null, "ml"));
            Assert.IsNull(ServicioSugerenciasProductos.NombreConTamano(null, 500, "ml"));
        }

        // ---------------- Nesto#537 (segunda vuelta): «🚫 Quitar» = DescartadoVendedor ----------------

        [TestMethod]
        public async Task Responder_DescartadoVendedor_SeGuardaAparteYDevuelveLaSustitutaEnSuHueco()
        {
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            SugerenciaProductoDTO junto1 = primera.Sugerencias.Single(s => s.Producto == "JUNTO1");

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(junto1.IdSugerencia,
                new RespuestaSugerenciaDTO { Respuesta = "descartadovendedor" });

            Assert.AreEqual(RespuestasSugerencia.DESCARTADO_VENDEDOR, r.Respuesta);
            Assert.AreEqual(RespuestasSugerencia.DESCARTADO_VENDEDOR, _mostradas.Single(m => m.Id == junto1.IdSugerencia).Respuesta,
                "Aparte del rechazo del cliente");
            A.CallTo(() => _repo.GuardarDescartadoVendedorProvisional(A<int>._)).MustNotHaveHappened();
            Assert.IsNotNull(r.Sustituta);
            Assert.AreEqual("JUNTO2", r.Sustituta.Producto);
            Assert.AreEqual(junto1.Posicion, r.Sustituta.Posicion);
            CollectionAssert.AreEqual(new[] { "JUNTO2", "LETOCA1", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Responder_DescartadoVendedorSinElScript_GuardaNoLeInteresaConLaMarcaYNoFalla()
        {
            _admiteDescartadoVendedor = false;
            SugerenciasProductosDTO primera = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            SugerenciaProductoDTO junto1 = primera.Sugerencias.Single(s => s.Producto == "JUNTO1");

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(junto1.IdSugerencia,
                new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR });

            A.CallTo(() => _repo.GuardarRespuesta(A<int>._, RespuestasSugerencia.DESCARTADO_VENDEDOR)).MustNotHaveHappened();
            SugerenciaMostrada guardada = _mostradas.Single(m => m.Id == junto1.IdSugerencia);
            Assert.AreEqual(RespuestasSugerencia.NO_LE_INTERESA, guardada.Respuesta, "Lo que admite el CK de antes");
            StringAssert.StartsWith(guardada.Texto, RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR, "El script lo reclasifica");
            Assert.AreEqual(RespuestasSugerencia.DESCARTADO_VENDEDOR, r.Respuesta, "Al cliente se le contesta lo que pidió");
            Assert.AreEqual("JUNTO2", r.Sustituta?.Producto);
        }

        [TestMethod]
        public async Task DescartadoVendedor_NoSeVuelveASugerirAEseClienteEnSesentaDias()
        {
            // El SQL de los excluidos cuenta los dos (y ningún otro valor)
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "'NoLeInteresa', 'DescartadoVendedor'");
            Assert.IsTrue(RespuestasSugerencia.ExcluyenSesentaDias(RespuestasSugerencia.DESCARTADO_VENDEDOR));
            Assert.IsTrue(RespuestasSugerencia.ExcluyenSesentaDias(RespuestasSugerencia.NO_LE_INTERESA));
            Assert.IsFalse(RespuestasSugerencia.ExcluyenSesentaDias(RespuestasSugerencia.ANADIDO));
            Assert.AreEqual(60, MotorSugerenciasProductos.DIAS_SIN_REPETIR_RECHAZO);

            // Otra plantilla (sesión nueva) del mismo cliente: lo que devuelve LeerRechazados no sale
            _rechazados.Add("JUNTO1");
            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null, "CARRO1"), "carlos");
            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "JUNTO1"));
            A.CallTo(() => _repo.LeerRechazados("1", CLIENTE, HOY.Date.AddDays(-60))).MustHaveHappened();
        }

        [TestMethod]
        public void SqlProvisional_LlevaLaMarcaQueQuitaElScript()
        {
            StringAssert.Contains(SugerenciasProductosSql.SQL_GUARDAR_DESCARTADO_VENDEDOR_PROVISIONAL, "'NoLeInteresa'");
            StringAssert.Contains(SugerenciasProductosSql.SQL_GUARDAR_DESCARTADO_VENDEDOR_PROVISIONAL, RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR);
            Assert.AreEqual(21, RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR.Length, "El script Issue670b quita 21 caracteres (SUBSTRING(Texto, 22, 200))");
            Assert.IsTrue(RespuestasSugerencia.DESCARTADO_VENDEDOR.Length <= 20, "Cabe en Respuesta varchar(20)");
        }

        // ---------------- Ayudas ----------------

        private CalcularSugerenciasDTO Peticion(Guid? idSesion, params string[] productos) => Peticion(idSesion, productos, "SAN");

        private CalcularSugerenciasDTO PeticionDeVendedor(Guid? idSesion, string producto, string vendedor) => Peticion(idSesion, new[] { producto }, vendedor);

        private static CalcularSugerenciasDTO Peticion(Guid? idSesion, string[] productos, string vendedor) => new CalcularSugerenciasDTO
        {
            Empresa = "1",
            Cliente = CLIENTE,
            Contacto = "0",
            Vendedor = vendedor,
            IdSesion = idSesion,
            Aplicacion = AplicacionesSugerencias.NESTO,
            Productos = productos.Select(p => new ProductoCarritoSugerenciaDTO { Producto = p, Cantidad = 1 }).ToList()
        };

        private void Ficha(string producto, string grupo, string subGrupo) => _fichas[producto] = NuevaFicha(producto, grupo, subGrupo, nombre: "Nombre de " + producto);

        private static FichaProductoSugerencia NuevaFicha(string producto, string grupo, string subGrupo, short estado = 0, bool ficticio = false, string nombre = null)
            => new FichaProductoSugerencia { Producto = producto, Nombre = nombre ?? producto, Grupo = grupo, SubGrupo = subGrupo, Estado = estado, Ficticio = ficticio };

        private static Dictionary<string, FichaProductoSugerencia> FichasCos(params string[] productos)
            => productos.ToDictionary(p => p, p => NuevaFicha(p, "COS", "CRE"), StringComparer.OrdinalIgnoreCase);

        private static ParCompradoJunto Par(string a, string b, decimal confianza)
            => new ParCompradoJunto { ProductoA = a, ProductoB = b, Confianza = confianza, Lift = 2m, VecesJuntos = 10, Soporte = 0.01m };

        private static CandidatoSugerencia Candidato(string producto, string motivo, decimal puntuacion)
            => new CandidatoSugerencia { Producto = producto, Motivo = motivo, Puntuacion = puntuacion, Texto = producto };

        private void Compras(string producto, params int[] dias) => _compras.AddRange(Dias(producto, dias).Select(c =>
        {
            c.BaseImponible = producto == "CARRO1" ? 1000 : 10;
            return c;
        }));

        private static IEnumerable<CompraDiaReposicion> Dias(string producto, params int[] dias)
            => dias.Select(d => new CompraDiaReposicion { Cliente = CLIENTE, Producto = producto, Dia = HOY.Date.AddDays(d), BaseImponible = 10, Cantidad = 1 }).ToList();

        private static IEnumerable<CompraDiaReposicion> Cesta(string cliente, int dia, params string[] productos)
            => productos.Select(p => new CompraDiaReposicion { Cliente = cliente, Producto = p, Dia = HOY.Date.AddDays(dia), BaseImponible = 10, Cantidad = 1 }).ToList();

        private static HashSet<string> Conjunto(params string[] productos) => new HashSet<string>(productos, StringComparer.OrdinalIgnoreCase);

        private static SugerenciaMostrada Copia(SugerenciaMostrada m) => new SugerenciaMostrada
        {
            Id = m.Id,
            SesionId = m.SesionId,
            Producto = m.Producto,
            Motivo = m.Motivo,
            Puntuacion = m.Puntuacion,
            Posicion = m.Posicion,
            VersionMotor = m.VersionMotor,
            Texto = m.Texto,
            FechaMostrada = m.FechaMostrada,
            Respuesta = m.Respuesta,
            FechaRespuesta = m.FechaRespuesta,
            EntroEnPedido = m.EntroEnPedido,
            CantidadEnPedido = m.CantidadEnPedido,
            Fuentes = m.Fuentes,
            TamanoCarrito = m.TamanoCarrito,
            Exploracion = m.Exploracion
        };
    }
}
