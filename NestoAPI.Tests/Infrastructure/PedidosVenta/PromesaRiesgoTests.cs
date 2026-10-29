using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Tests.Infrastructure.Stocks;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Repositorio en memoria de PedidosPromesaRiesgo (y de los pedidos con promesa) para los tests de NestoAPI#693.</summary>
    internal class RepositorioPromesaRiesgoEnMemoria : IRepositorioPromesaRiesgo
    {
        public bool Existe { get; set; } = true;
        /// <summary>Pedido → (prometida, productos pendientes).</summary>
        public Dictionary<int, (DateTime prometida, List<string> productos)> Pedidos { get; } = new Dictionary<int, (DateTime, List<string>)>();
        public List<RiesgoPromesa> Filas { get; } = new List<RiesgoPromesa>();
        public List<List<string>> ConsultasCandidatos { get; } = new List<List<string>>();
        private int siguienteId = 1;

        public bool TablaExiste() => Existe;

        public Task<List<PedidoConPromesa>> LeerCandidatos(string empresa, IEnumerable<string> productos, DateTime hoy)
        {
            List<string> lista = productos?.ToList();
            ConsultasCandidatos.Add(lista);
            return Task.FromResult(Pedidos
                .Where(p => (p.Value.prometida.Date >= hoy.Date || Filas.Any(f => f.Pedido == p.Key && f.Resuelto == null))
                    && (lista == null || p.Value.productos.Any(x => lista.Contains(x, StringComparer.OrdinalIgnoreCase))))
                .Select(p => new PedidoConPromesa { Empresa = empresa, Pedido = p.Key, FechaPrometida = p.Value.prometida })
                .ToList());
        }

        public Task<List<RiesgoPromesa>> LeerSinResolver(string empresa, IEnumerable<int> pedidos)
        {
            List<int> lista = pedidos?.ToList();
            return Task.FromResult(Filas.Where(f => f.Resuelto == null && (lista == null || lista.Contains(f.Pedido))).Select(Copia).ToList());
        }

        public Task<(DateTime? prometida, bool quedaPendiente)> LeerEstadoPedido(string empresa, int pedido) =>
            Task.FromResult(Pedidos.TryGetValue(pedido, out var p) ? ((DateTime?)p.prometida, p.productos.Any()) : ((DateTime?)null, false));

        public Task<Dictionary<string, string>> LeerNombresProductos(string empresa, IEnumerable<string> productos) =>
            Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["42485"] = "PARCHES DE COLÁGENO" });

        public Task<int> Insertar(RiesgoPromesa riesgo)
        {
            RiesgoPromesa fila = Copia(riesgo);
            fila.Id = siguienteId++;
            Filas.Add(fila);
            return Task.FromResult(fila.Id);
        }

        public Task Actualizar(RiesgoPromesa riesgo)
        {
            RiesgoPromesa fila = Filas.Single(f => f.Id == riesgo.Id);
            fila.FechaNueva = riesgo.FechaNueva;
            fila.Motivo = riesgo.Motivo;
            fila.Evento = riesgo.Evento;
            fila.Actualizado = riesgo.Actualizado;
            fila.AvisadoCampana = riesgo.AvisadoCampana;
            fila.AvisadoCorreo = riesgo.AvisadoCorreo;
            return Task.CompletedTask;
        }

        public Task Resolver(int id, DateTime cuando, string motivo)
        {
            RiesgoPromesa fila = Filas.Single(f => f.Id == id);
            fila.Resuelto = cuando;
            fila.MotivoResolucion = motivo;
            return Task.CompletedTask;
        }

        public Task MarcarAvisadoCampana(int id, DateTime cuando)
        {
            Filas.Single(f => f.Id == id).AvisadoCampana = cuando;
            return Task.CompletedTask;
        }

        public Task MarcarAvisadoCorreo(IEnumerable<int> ids, DateTime cuando)
        {
            foreach (int id in ids)
            {
                Filas.Single(f => f.Id == id).AvisadoCorreo = cuando;
            }
            return Task.CompletedTask;
        }

        public Task<List<RiesgoPromesa>> LeerSinCorreo(string empresa) =>
            Task.FromResult(Filas.Where(f => f.Resuelto == null && f.AvisadoCorreo == null).Select(Copia).ToList());

        public Task<List<RiesgoPromesaListado>> LeerListado(string empresa, bool incluirResueltos = false) => Task.FromResult(new List<RiesgoPromesaListado>());

        private static RiesgoPromesa Copia(RiesgoPromesa r) => new RiesgoPromesa
        {
            Id = r.Id,
            Empresa = r.Empresa,
            Pedido = r.Pedido,
            FechaPrometida = r.FechaPrometida,
            FechaNueva = r.FechaNueva,
            Motivo = r.Motivo,
            Evento = r.Evento,
            Detectado = r.Detectado,
            Actualizado = r.Actualizado,
            AvisadoCampana = r.AvisadoCampana,
            AvisadoCorreo = r.AvisadoCorreo,
            Resuelto = r.Resuelto,
            MotivoResolucion = r.MotivoResolucion
        };
    }

    [TestClass]
    public class PromesaRiesgoTests
    {
        // Jueves 22/10/2026: la entrega a la agencia prometida del pedido 928908 (cliente 40353).
        private static readonly DateTime PROMETIDA = new DateTime(2026, 10, 22);
        private static readonly DateTime VIERNES = new DateTime(2026, 10, 23);
        // Miércoles 21/10/2026 por la tarde: Reina termina la reposición 81094.
        private static readonly DateTime AHORA = new DateTime(2026, 10, 21, 17, 30, 0);
        private const int PEDIDO = 928908;

        private RepositorioPromesaRiesgoEnMemoria repositorio;
        private IServicioFechaEntregaAgencia fechas;
        private IAvisadorPromesaRiesgo avisador;
        private ModoAvisoPromesaRiesgo modo;
        private FechaEntregaAgenciaDTO calculoActual;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioPromesaRiesgoEnMemoria();
            repositorio.Pedidos[PEDIDO] = (PROMETIDA, new List<string> { "42485" });
            fechas = A.Fake<IServicioFechaEntregaAgencia>();
            A.CallTo(() => fechas.CalcularPedido(A<string>._, A<int>._)).ReturnsLazily(() => Task.FromResult(calculoActual));
            avisador = A.Fake<IAvisadorPromesaRiesgo>();
            A.CallTo(() => avisador.Preparar(A<RiesgoPromesa>._, A<bool>._))
                .ReturnsLazily((RiesgoPromesa r, bool e) => new AvisoPromesaRiesgo { Riesgo = r, Empeora = e, Cliente = "40353" });
            A.CallTo(() => avisador.AvisarCampana(A<AvisoPromesaRiesgo>._)).Returns(Task.FromResult(3));
            modo = ModoAvisoPromesaRiesgo.Activo;
            calculoActual = CumpleElJueves();
        }

        private static readonly Func<ModoAvisoPromesaRiesgo> leerModoOriginal = EventosPromesaRiesgo.LeerModo;
        private static readonly Action<EventoPromesaRiesgo, TimeSpan> programarOriginal = EventosPromesaRiesgo.Programar;

        [TestCleanup]
        public void Limpiar()
        {
            EventosPromesaRiesgo.LeerModo = leerModoOriginal;
            EventosPromesaRiesgo.Programar = programarOriginal;
            InterruptorAvisoPromesaRiesgo.LeerAjuste = null;
        }

        private ServicioPromesaRiesgo Servicio() => new ServicioPromesaRiesgo(repositorio, fechas, avisador, () => AHORA, () => modo, _ => { });

        // ------------------------------------------------------------------ cálculos del motor (lo que devolvería #606)

        private static FechaEntregaAgenciaDTO CumpleElJueves() => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = PROMETIDA,
            EntregaCompleta = PROMETIDA,
            Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA,
            Desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Producto = "42485", Unidades = 3, Tienda = "REI", PuedeSalirEl = PROMETIDA }
            }
        };

        /// <summary>Reina mandó 1 de 3: la que viene de camino sale el jueves, las otras 2 (fantasma en Reina) con la próxima reposición.</summary>
        private static FechaEntregaAgenciaDTO RetrasoAlViernes() => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = VIERNES,
            EntregaCompleta = VIERNES,
            Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA,
            Desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, Producto = "42485", Unidades = 1, PuedeSalirEl = PROMETIDA },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Producto = "42485", Unidades = 2, Tienda = "REI", PuedeSalirEl = VIERNES }
            }
        };

        private static FechaEntregaAgenciaDTO SinFecha() => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = null,
            Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA,
            Desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = "42485", Unidades = 3 }
            }
        };

        private static EventoPromesaRiesgo TerminarReposicion81094() => new EventoPromesaRiesgo
        {
            Empresa = "1",
            Descripcion = "reposición 81094 de Reina a Algete",
            Productos = new List<string> { "42485" },
            Causas = new Dictionary<string, CausaPromesaRiesgo>(StringComparer.OrdinalIgnoreCase)
            {
                ["42485"] = new CausaPromesaRiesgo { Tienda = "REI", Mandadas = 1, Traspaso = 81094 }
            }
        };

        private static EventoPromesaRiesgo Evento(string descripcion, params string[] productos) =>
            new EventoPromesaRiesgo { Empresa = "1", Descripcion = descripcion, Productos = productos.ToList() };

        // ------------------------------------------------------------------ evaluador

        [TestMethod]
        public void Evaluador_SinRegistrarYCumple_NoHaceNada()
        {
            Assert.AreEqual(AccionPromesaRiesgo.Nada, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, CumpleElJueves(), true, null).Accion);
        }

        [TestMethod]
        public void Evaluador_SeRetrasa_SeRegistra()
        {
            Assert.AreEqual(AccionPromesaRiesgo.Registrar, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, RetrasoAlViernes(), true, null).Accion);
        }

        [TestMethod]
        public void Evaluador_SeQuedaSinFecha_SeRegistra()
        {
            Assert.AreEqual(AccionPromesaRiesgo.Registrar, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, SinFecha(), true, null).Accion);
        }

        [TestMethod]
        public void Evaluador_SinFechaPeroSinNadaQueSalgaPorAgencia_NoEstaEnRiesgo()
        {
            var deTienda = new FechaEntregaAgenciaDTO { FechaEntregaAgencia = null };
            Assert.IsFalse(EvaluadorPromesaRiesgo.EstaEnRiesgo(PROMETIDA, deTienda));
        }

        [TestMethod]
        public void Evaluador_RegistradoYSigueIgual_NoSeVuelveAAvisar()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES };
            Assert.AreEqual(AccionPromesaRiesgo.Actualizar, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, RetrasoAlViernes(), true, registrado).Accion);
        }

        [TestMethod]
        public void Evaluador_RegistradoConFechaYSeQuedaSinFecha_Empeora()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES };
            Assert.AreEqual(AccionPromesaRiesgo.Empeora, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, SinFecha(), true, registrado).Accion);
        }

        [TestMethod]
        public void Evaluador_RegistradoSinFecha_NadaEsPeor()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = null };
            Assert.AreEqual(AccionPromesaRiesgo.Actualizar, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, SinFecha(), true, registrado).Accion);
            Assert.AreEqual(AccionPromesaRiesgo.Actualizar, EvaluadorPromesaRiesgo.Decidir(PROMETIDA, RetrasoAlViernes(), true, registrado).Accion);
        }

        [TestMethod]
        public void Evaluador_RegistradoYVuelveACumplir_SeResuelve()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES };
            DecisionPromesaRiesgo decision = EvaluadorPromesaRiesgo.Decidir(PROMETIDA, CumpleElJueves(), true, registrado);
            Assert.AreEqual(AccionPromesaRiesgo.Resolver, decision.Accion);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_CUMPLE, decision.MotivoResolucion);
        }

        [TestMethod]
        public void Evaluador_RegistradoYServido_SeResuelve()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES };
            DecisionPromesaRiesgo decision = EvaluadorPromesaRiesgo.Decidir(PROMETIDA, null, false, registrado);
            Assert.AreEqual(AccionPromesaRiesgo.Resolver, decision.Accion);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_SERVIDO, decision.MotivoResolucion);
        }

        [TestMethod]
        public void Evaluador_FechaRenegociada_SeResuelveYSeMiraLaNueva()
        {
            var registrado = new RiesgoPromesa { Id = 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES };
            DecisionPromesaRiesgo cumple = EvaluadorPromesaRiesgo.Decidir(VIERNES, RetrasoAlViernes(), true, registrado);
            Assert.AreEqual(AccionPromesaRiesgo.Resolver, cumple.Accion);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_RENEGOCIADO, cumple.MotivoResolucion);
            Assert.AreEqual(AccionPromesaRiesgo.Nada, cumple.AccionTrasRenegociar);

            DecisionPromesaRiesgo noCumple = EvaluadorPromesaRiesgo.Decidir(VIERNES, SinFecha(), true, registrado);
            Assert.AreEqual(AccionPromesaRiesgo.Registrar, noCumple.AccionTrasRenegociar);
        }

        // ------------------------------------------------------------------ textos

        [TestMethod]
        public void TextoCausa_ReinaMandoMenos_DiceCuantasDeCuantas()
        {
            var causa = new CausaPromesaRiesgo { Tienda = "REI", Mandadas = 1, Traspaso = 81094 };
            Assert.AreEqual("Reina mandó 1 de 3 en la reposición 81094", TextosPromesaRiesgo.TextoCausa(causa, 3, PEDIDO));
        }

        [TestMethod]
        public void TextoCausa_OtroPedido_LoNombra_YElPropio_DiceQueSeHaModificado()
        {
            var causa = new CausaPromesaRiesgo { PedidoOrigen = 929104 };
            StringAssert.Contains(TextosPromesaRiesgo.TextoCausa(causa, 3, PEDIDO), "pedido 929104");
            Assert.AreEqual("se ha modificado el pedido", TextosPromesaRiesgo.TextoCausa(causa, 3, 929104));
        }

        [TestMethod]
        public void Motivo_DiceQueProductoFallaYPorQue()
        {
            var causas = new Dictionary<string, CausaPromesaRiesgo> { ["42485"] = new CausaPromesaRiesgo { Tienda = "REI", Mandadas = 1, Traspaso = 81094 } };
            string motivo = TextosPromesaRiesgo.Motivo(PEDIDO, PROMETIDA, RetrasoAlViernes(), causas,
                new Dictionary<string, int> { ["42485"] = 3 }, new Dictionary<string, string> { ["42485"] = "PARCHES" });

            Assert.AreEqual("Por qué: 42485 PARCHES (3 uds.): Reina mandó 1 de 3 en la reposición 81094; no está en Algete: tienen que venir de " +
                "Reina en la reposición y pueden salir el viernes 23/10/2026.", motivo);
            Assert.IsFalse(motivo.Contains("de camino"), "lo que sí llega a tiempo no se nombra: " + motivo);
        }

        // Carlos (23/10/26), correo «[Sombra] 2 pedidos ya no llegan a la fecha prometida»: en «Todo junto» todas las líneas salen el
        // día del pedido entero y, con la prometida pasada, hasta lo de Algete «sale después». Salían los 21 productos del 929104 y
        // no se veía que el culpable era 1 ud. del 43710 que venía de camino.
        private static PartidaFechaEntregaAgenciaDTO EnAlgete(string producto, DateTime sale) => new PartidaFechaEntregaAgenciaDTO
        {
            Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = producto, Unidades = 2, PuedeSalirEl = sale
        };

        [TestMethod]
        public void Motivo_TodoJunto_SoloNombraAlCulpableYDiceQueElRestoEstaListo()
        {
            var lunes = new DateTime(2026, 10, 26);
            var calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = lunes,
                Motivo = "Sale entero a la agencia el lunes 26/10. Modo «Todo junto»: sale cuando esté todo. Lo que hay en Algete está listo.",
                Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    EnAlgete("17404", VIERNES), EnAlgete("32564", VIERNES),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, Producto = "43710", Unidades = 1, PuedeSalirEl = lunes }
                }
            };

            string motivo = TextosPromesaRiesgo.Motivo(929104, PROMETIDA, calculo, null, null, new Dictionary<string, string> { ["43710"] = "BRILLO" }, VIERNES);

            Assert.AreEqual("Por qué: 43710 BRILLO (1 ud.): no está en Algete: viene de camino desde una tienda y puede salir el lunes 26/10/2026. " +
                "El resto está listo en Algete, pero el pedido es «Todo junto» y espera a todo.", motivo);
        }

        [TestMethod]
        public void Motivo_TodoEnAlgeteConFechaDeEntregaPosterior_LoDiceDelPedido()
        {
            var lunes = new DateTime(2026, 10, 26);
            var calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = lunes,
                Motivo = "Sale entero a la agencia el lunes 26/10. Modo «Todo junto»: sale cuando esté todo. Tiene fecha de entrega el lunes 26/10: " +
                    "no entra en el picking hasta ese día. Lo que hay en Algete está listo.",
                Desglose = new List<PartidaFechaEntregaAgenciaDTO> { EnAlgete("17404", lunes), EnAlgete("24868", lunes) }
            };

            string motivo = TextosPromesaRiesgo.Motivo(929073, PROMETIDA, calculo, null, null, null, VIERNES);

            Assert.AreEqual("Por qué: el pedido tiene fecha de entrega el lunes 26/10: no entra en el picking hasta ese día.", motivo);
        }

        [TestMethod]
        public void Motivo_TodoEnAlgeteYLaPrometidaYaPaso_DiceQueNoSalio()
        {
            var calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = VIERNES,
                Motivo = "Sale entero a la agencia el viernes 23/10. Modo «Todo junto»: sale cuando esté todo. Lo que hay en Algete está listo.",
                Desglose = new List<PartidaFechaEntregaAgenciaDTO> { EnAlgete("23474", VIERNES), EnAlgete("27593", VIERNES) }
            };

            string motivo = TextosPromesaRiesgo.Motivo(929077, PROMETIDA, calculo, null, null, null, VIERNES);

            Assert.AreEqual("Por qué: todo está listo en Algete, pero no salió el jueves 22/10/2026.", motivo);
        }

        [TestMethod]
        public void Motivo_Proveedor_SoloElQueFallaYLoQueLlegaATiempoNoSeNombra()
        {
            var martes = new DateTime(2026, 10, 27);
            var calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = new DateTime(2026, 11, 9),
                Motivo = "Sale entero a la agencia el lunes 09/11. Modo «Todo junto»: sale cuando esté todo.",
                Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    EnAlgete("35894", martes),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Producto = "41260", Unidades = 1, Tienda = "REI", PuedeSalirEl = martes },
                    new PartidaFechaEntregaAgenciaDTO
                    {
                        Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "42652", Unidades = 5, PedidoProveedor = 220672,
                        FechaPrevistaProveedor = new DateTime(2026, 11, 6), PuedeSalirEl = new DateTime(2026, 11, 9)
                    }
                }
            };

            string motivo = TextosPromesaRiesgo.Motivo(928370, martes, calculo, null, null, null, VIERNES);

            Assert.AreEqual("Por qué: 42652 (5 uds.): no hay stock: llegan con el pedido al proveedor 220672, previsto el viernes 06/11/2026, " +
                "y pueden salir el lunes 09/11/2026. El resto está listo en Algete, pero el pedido es «Todo junto» y espera a todo.", motivo);
        }

        [TestMethod]
        public void Motivo_ProveedorQueNoHaLlegado_LoDice()
        {
            var calculo = new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = null,
                Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    new PartidaFechaEntregaAgenciaDTO
                    {
                        Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "42652", Unidades = 1, PedidoProveedor = 220672,
                        FechaPrevistaProveedor = new DateTime(2026, 10, 20), LlegadaSupuesta = new DateTime(2026, 10, 27)
                    }
                }
            };

            string motivo = TextosPromesaRiesgo.Motivo(928370, PROMETIDA, calculo, null, null, null, VIERNES);

            Assert.AreEqual("Por qué: 42652 (1 ud.): no hay stock y el pedido al proveedor 220672 tenía que haber llegado el martes 20/10/2026 y no ha llegado.", motivo);
        }

        [TestMethod]
        public void Notificacion_DeUsted_ConPedidoClienteYFechas()
        {
            var aviso = new AvisoPromesaRiesgo
            {
                Cliente = "40353",
                NombreCliente = "BELLPROMUR",
                Riesgo = new RiesgoPromesa { Empresa = "1", Pedido = PEDIDO, FechaPrometida = PROMETIDA, FechaNueva = null, Motivo = "42485 (3 uds.): 3 sin fecha: no hay stock ni fecha de llegada." }
            };
            NotificacionPushDTO n = TextosPromesaRiesgo.Notificacion(aviso);

            Assert.AreEqual("El pedido 928908 se queda sin fecha", n.Titulo);
            StringAssert.Contains(n.Cuerpo, "cliente 40353 - BELLPROMUR");
            StringAssert.Contains(n.Cuerpo, "jueves 22/10/2026");
            StringAssert.Contains(n.Cuerpo, "ahora se queda sin fecha");
            StringAssert.Contains(n.Cuerpo, "Revise el pedido");
            Assert.AreEqual(TextosPromesaRiesgo.TIPO_NOTIFICACION, n.Tipo);
            Assert.AreEqual("928908", n.Datos["pedido"]);
        }

        // ------------------------------------------------------------------ servicio

        [TestMethod]
        public async Task Evento_SoloRecalculaLosPedidosQueLlevanLosProductos()
        {
            repositorio.Pedidos[929000] = (PROMETIDA, new List<string> { "OTRO" });

            await Servicio().ProcesarEvento(Evento("movimiento de stock (diario General)", "42485"));

            CollectionAssert.AreEqual(new[] { "42485" }, repositorio.ConsultasCandidatos.Single());
            A.CallTo(() => fechas.CalcularPedido("1", PEDIDO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fechas.CalcularPedido(A<string>._, 929000)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Evento_SinProductos_NoLeeNada()
        {
            await Servicio().ProcesarEvento(Evento("nada"));
            Assert.AreEqual(0, repositorio.ConsultasCandidatos.Count);
        }

        [TestMethod]
        public async Task Apagado_NoRecalculaNada()
        {
            modo = ModoAvisoPromesaRiesgo.Apagado;
            calculoActual = SinFecha();

            await Servicio().ProcesarEvento(TerminarReposicion81094());

            Assert.AreEqual(0, repositorio.Filas.Count);
            A.CallTo(() => fechas.CalcularPedido(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinTabla_NoHaceNada()
        {
            repositorio.Existe = false;
            calculoActual = SinFecha();
            await Servicio().ProcesarEvento(TerminarReposicion81094());
            A.CallTo(() => fechas.CalcularPedido(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sombra_RegistraPeroNoAvisa()
        {
            modo = ModoAvisoPromesaRiesgo.Sombra;
            calculoActual = RetrasoAlViernes();

            ResultadoPromesaRiesgo resultado = await Servicio().ProcesarEvento(TerminarReposicion81094());

            Assert.AreEqual(1, resultado.Registrados);
            Assert.AreEqual(1, repositorio.Filas.Count);
            Assert.IsNull(repositorio.Filas[0].AvisadoCampana);
            A.CallTo(() => avisador.AvisarCampana(A<AvisoPromesaRiesgo>._)).MustNotHaveHappened();
        }

        /// <summary>
        /// El caso real 928908 (Carlos, 22/10/26): Todo junto, 42485 ×3 esperado de la reposición de Reina del 21/10 para
        /// salir el jueves 22/10. Reina mandó 1 de 3 (81094), Alcobendas regularizó −2 y la unidad que llegó a Algete salió en
        /// otro pedido. El pedido pasa a salir más tarde en cuanto se termina la reposición (adelantarse) y se avisa UNA vez
        /// aunque después lleguen más movimientos que no lo empeoran.
        /// </summary>
        [TestMethod]
        public async Task Caso928908_RetrasoDetectadoAlTerminarLaReposicion_SeAvisaUnaSolaVez()
        {
            ServicioPromesaRiesgo servicio = Servicio();
            calculoActual = RetrasoAlViernes();

            ResultadoPromesaRiesgo terminar = await servicio.ProcesarEvento(TerminarReposicion81094());
            ResultadoPromesaRiesgo regularizacion = await servicio.ProcesarEvento(Evento("movimiento de stock (diario Inventario)", "42485"));
            ResultadoPromesaRiesgo otroPedido = await servicio.ProcesarEvento(new EventoPromesaRiesgo
            {
                Empresa = "1",
                Descripcion = "pedido 929104 grabado",
                Productos = new List<string> { "42485" },
                Causas = new Dictionary<string, CausaPromesaRiesgo> { ["42485"] = new CausaPromesaRiesgo { PedidoOrigen = 929104 } }
            });
            ResultadoPromesaRiesgo nocturna = await servicio.PasadaNocturna("1");

            Assert.AreEqual(1, terminar.AvisadosCampana);
            Assert.AreEqual(0, regularizacion.AvisadosCampana + otroPedido.AvisadosCampana + nocturna.AvisadosCampana);
            A.CallTo(() => avisador.AvisarCampana(A<AvisoPromesaRiesgo>._)).MustHaveHappenedOnceExactly();
            RiesgoPromesa fila = repositorio.Filas.Single();
            Assert.AreEqual(PROMETIDA, fila.FechaPrometida);
            Assert.AreEqual(VIERNES, fila.FechaNueva);
            Assert.IsNotNull(fila.AvisadoCampana);
            StringAssert.Contains(fila.Motivo, "Reina mandó 1 de 3 en la reposición 81094", "se conserva el porqué del evento que lo detectó");
            Assert.AreEqual("reposición 81094 de Reina a Algete", fila.Evento);
        }

        [TestMethod]
        public async Task Caso928908_SiDespuesSeQuedaSinFecha_SeVuelveAAvisarPorqueEmpeora()
        {
            ServicioPromesaRiesgo servicio = Servicio();
            calculoActual = RetrasoAlViernes();
            _ = await servicio.ProcesarEvento(TerminarReposicion81094());

            calculoActual = SinFecha();
            ResultadoPromesaRiesgo resultado = await servicio.ProcesarEvento(Evento("movimiento de stock (diario Inventario)", "42485"));
            ResultadoPromesaRiesgo otraVez = await servicio.ProcesarEvento(Evento("movimiento de stock (diario General)", "42485"));

            Assert.AreEqual(1, resultado.Empeorados);
            Assert.AreEqual(1, resultado.AvisadosCampana);
            Assert.AreEqual(0, otraVez.AvisadosCampana);
            A.CallTo(() => avisador.AvisarCampana(A<AvisoPromesaRiesgo>.That.Matches(a => a.Empeora))).MustHaveHappenedOnceExactly();
            RiesgoPromesa fila = repositorio.Filas.Single();
            Assert.IsNull(fila.FechaNueva);
            Assert.IsNull(fila.AvisadoCorreo, "vuelve a salir en el correo resumen");
        }

        [TestMethod]
        public async Task VuelveACumplir_SeResuelveSolo()
        {
            ServicioPromesaRiesgo servicio = Servicio();
            calculoActual = RetrasoAlViernes();
            _ = await servicio.ProcesarEvento(TerminarReposicion81094());

            calculoActual = CumpleElJueves();
            ResultadoPromesaRiesgo resultado = await servicio.ProcesarEvento(Evento("compra recibida", "42485"));

            Assert.AreEqual(1, resultado.Resueltos);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_CUMPLE, repositorio.Filas.Single().MotivoResolucion);
        }

        [TestMethod]
        public async Task PasadaNocturna_ResuelveLosServidos()
        {
            calculoActual = RetrasoAlViernes();
            ServicioPromesaRiesgo servicio = Servicio();
            _ = await servicio.ProcesarEvento(TerminarReposicion81094());

            repositorio.Pedidos.Remove(PEDIDO); // albaranado: ya no es candidato ni le queda nada pendiente
            ResultadoPromesaRiesgo resultado = await servicio.PasadaNocturna("1");

            Assert.AreEqual(1, resultado.Resueltos);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_SERVIDO, repositorio.Filas.Single().MotivoResolucion);
            Assert.IsNull(repositorio.ConsultasCandidatos.Last(), "la nocturna mira todos los pedidos con promesa");
        }

        // ------------------------------------------------------------------ correo resumen

        [TestMethod]
        public async Task CorreoResumen_MandaLoNuevoUnaVezYLoMarca()
        {
            calculoActual = RetrasoAlViernes();
            _ = await Servicio().ProcesarEvento(TerminarReposicion81094());

            int primera = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");
            int segunda = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");

            Assert.AreEqual(1, primera);
            Assert.AreEqual(0, segunda);
            A.CallTo(() => avisador.EnviarCorreos(A<IEnumerable<AvisoPromesaRiesgo>>._, false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RevisarAntesDelCorreo_SiYaCumple_SeResuelveYNoSaleEnElCorreo()
        {
            // NestoAPI#730 (853): detectado por la mañana; un inventario a mediodía lo arregla y no pasa por la API.
            calculoActual = RetrasoAlViernes();
            _ = await Servicio().ProcesarEvento(TerminarReposicion81094());
            calculoActual = CumpleElJueves();

            ResultadoPromesaRiesgo revision = await Servicio().RevisarAntesDelCorreo("1");
            int enviados = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");

            Assert.AreEqual(1, revision.Resueltos);
            Assert.AreEqual(EvaluadorPromesaRiesgo.RESUELTO_CUMPLE, repositorio.Filas.Single().MotivoResolucion);
            Assert.AreEqual(0, enviados, "ya no está en riesgo: no se manda lo guardado");
        }

        [TestMethod]
        public async Task RevisarAntesDelCorreo_SiCambiaLaFecha_ActualizaElPorqueYSaleConElNuevo()
        {
            calculoActual = SinFecha();
            _ = await Servicio().ProcesarEvento(TerminarReposicion81094());
            string motivoGuardado = repositorio.Filas.Single().Motivo;
            calculoActual = RetrasoAlViernes();

            ResultadoPromesaRiesgo revision = await Servicio().RevisarAntesDelCorreo("1");
            int enviados = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");

            Assert.AreEqual(1, revision.Recalculados);
            Assert.AreEqual(VIERNES, repositorio.Filas.Single().FechaNueva);
            Assert.AreNotEqual(motivoGuardado, repositorio.Filas.Single().Motivo);
            Assert.AreEqual(1, enviados);
        }

        [TestMethod]
        public async Task RevisarAntesDelCorreo_LosYaEnviadosYApagado_NoSeTocan()
        {
            calculoActual = RetrasoAlViernes();
            _ = await Servicio().ProcesarEvento(TerminarReposicion81094());
            _ = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");
            calculoActual = CumpleElJueves();

            ResultadoPromesaRiesgo yaEnviado = await Servicio().RevisarAntesDelCorreo("1");
            modo = ModoAvisoPromesaRiesgo.Apagado;
            ResultadoPromesaRiesgo apagado = await Servicio().RevisarAntesDelCorreo("1");

            Assert.AreEqual(0, yaEnviado.Recalculados, "solo lo que va a salir en el correo");
            Assert.AreEqual(0, apagado.Recalculados);
            Assert.IsNull(repositorio.Filas.Single().Resuelto);
        }

        [TestMethod]
        public async Task CorreoResumen_EnSombra_VaComoSombra()
        {
            modo = ModoAvisoPromesaRiesgo.Sombra;
            calculoActual = RetrasoAlViernes();
            _ = await Servicio().ProcesarEvento(TerminarReposicion81094());

            _ = await PromesaRiesgoJobsService.CorreoResumen(repositorio, avisador, () => AHORA, () => modo, "1");

            A.CallTo(() => avisador.EnviarCorreos(A<IEnumerable<AvisoPromesaRiesgo>>._, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void AgruparPorCorreo_CadaDestinatarioRecibeSusPedidos()
        {
            var a = new AvisoPromesaRiesgo { Riesgo = new RiesgoPromesa { Pedido = 1 } };
            var b = new AvisoPromesaRiesgo { Riesgo = new RiesgoPromesa { Pedido = 2 } };
            var correos = new Dictionary<int, string[]>
            {
                [1] = new[] { "vendedor1@nuevavision.es", "jefe@nuevavision.es", "carlosadrian@nuevavision.es" },
                [2] = new[] { "vendedor2@nuevavision.es", "jefe@nuevavision.es", "carlosadrian@nuevavision.es" }
            };

            Dictionary<string, List<AvisoPromesaRiesgo>> porCorreo = AvisadorPromesaRiesgo.AgruparPorCorreo(new[] { a, b }, x => correos[x.Riesgo.Pedido]);

            Assert.AreEqual(4, porCorreo.Count);
            CollectionAssert.AreEqual(new[] { a }, porCorreo["vendedor1@nuevavision.es"]);
            CollectionAssert.AreEqual(new[] { a, b }, porCorreo["jefe@nuevavision.es"]);
            CollectionAssert.AreEqual(new[] { a, b }, porCorreo["carlosadrian@nuevavision.es"]);
        }

        // ------------------------------------------------------------------ destinatarios

        [TestMethod]
        public void Destinatarios_VendedorJefeDeEquipoYDireccion_SinRepetir()
        {
            var comerciales = new ComercialesPedido
            {
                Pedido = PEDIDO,
                UsuarioPedido = "Laura",
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "JGP", Correo = "javier@nuevavision.es", Usuarios = new List<string> { "Javier" } }
                }
            };
            var jefe = new ComercialesPedido
            {
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "ASH", Correo = "alberto@nuevavision.es", Usuarios = new List<string> { "Alberto" } }
                }
            };

            DestinatariosPromesaRiesgo d = AvisadorPromesaRiesgo.Componer(comerciales, new[] { jefe }, new[] { "Carlos", "Manuel", "javier" },
                new[] { "carlosadrian@nuevavision.es", "manuelrodriguez@nuevavision.es" });

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Javier", "NUEVAVISION\\Alberto", "NUEVAVISION\\Carlos", "NUEVAVISION\\Manuel" }, d.Usuarios);
            CollectionAssert.AreEqual(new[] { "javier@nuevavision.es", "alberto@nuevavision.es", "carlosadrian@nuevavision.es", "manuelrodriguez@nuevavision.es" }, d.Correos);
        }

        // ---------- Carlos (28/10/26): cada usuario elige qué avisos recibe ----------

        [TestMethod]
        public void PreferenciaAvisos_NormalizaYDecideCanales()
        {
            Assert.AreEqual("Todo", PreferenciaAvisosPromesaRiesgo.Normalizar(null));
            Assert.AreEqual("Todo", PreferenciaAvisosPromesaRiesgo.Normalizar("lo que sea"));
            Assert.AreEqual("SoloCorreo", PreferenciaAvisosPromesaRiesgo.Normalizar(" solocorreo "));
            Assert.IsTrue(PreferenciaAvisosPromesaRiesgo.Campana("Todo") && PreferenciaAvisosPromesaRiesgo.Correo("Todo"));
            Assert.IsTrue(!PreferenciaAvisosPromesaRiesgo.Campana("SoloCorreo") && PreferenciaAvisosPromesaRiesgo.Correo("SoloCorreo"));
            Assert.IsTrue(PreferenciaAvisosPromesaRiesgo.Campana("SoloCampana") && !PreferenciaAvisosPromesaRiesgo.Correo("SoloCampana"));
            Assert.IsTrue(!PreferenciaAvisosPromesaRiesgo.Campana("Nada") && !PreferenciaAvisosPromesaRiesgo.Correo("Nada"));
            Assert.IsFalse(PreferenciaAvisosPromesaRiesgo.EsValido("Siempre"));
            Assert.AreEqual(4, AvisosPromesaRiesgoDTO.De("Nada").Opciones.Count);
            Assert.AreEqual("Nada (solo la ventana)", AvisosPromesaRiesgoDTO.De("nada").Texto);
        }

        [TestMethod]
        public void Destinatarios_ConPreferencias_CadaUnoRecibeLoQueEligio()
        {
            var comerciales = new ComercialesPedido
            {
                Pedido = PEDIDO,
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "IF", Correo = "javier@nuevavision.es", Usuarios = new List<string> { "Javier" } }
                }
            };
            var jefe = new ComercialesPedido
            {
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "ASH", Correo = "alberto@nuevavision.es", Usuarios = new List<string> { "Alberto" } } }
            };
            DestinatariosPromesaRiesgo todos = AvisadorPromesaRiesgo.Componer(comerciales, new[] { jefe }, new[] { "Carlos", "Manuel" },
                new[] { new KeyValuePair<string, string>("Carlos", "carlosadrian@nuevavision.es"), new KeyValuePair<string, string>("Manuel", "manuelrodriguez@nuevavision.es") });
            CollectionAssert.AreEqual(new[] { "Javier" }, todos.UsuariosDeCorreo["javier@nuevavision.es"]);
            CollectionAssert.AreEqual(new[] { "Carlos" }, todos.UsuariosDeCorreo["carlosadrian@nuevavision.es"]);

            var preferencias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Carlos"] = "SoloCorreo", ["Manuel"] = "Nada", ["Alberto"] = "SoloCampana"
            };
            DestinatariosPromesaRiesgo d = AvisadorPromesaRiesgo.AplicarPreferencias(todos, u => preferencias.TryGetValue(u, out string v) ? v : null);

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Javier", "NUEVAVISION\\Alberto" }, d.Usuarios, "Javier sin fila = Todo; Carlos solo correo; Manuel nada");
            CollectionAssert.AreEqual(new[] { "javier@nuevavision.es", "carlosadrian@nuevavision.es" }, d.Correos, "Alberto solo campana; Manuel nada");
        }

        [TestMethod]
        public void AplicarPreferencias_CorreoCompartido_SeQuedaSiAlgunoLoQuiere_YSinDueñoNoSeFiltra()
        {
            var d = new DestinatariosPromesaRiesgo();
            d.Correos.AddRange(new[] { "tiendaonline@nuevavision.es", "sindueño@nuevavision.es" });
            d.UsuariosDeCorreo["tiendaonline@nuevavision.es"] = new List<string> { "Laura", "Enrique" };
            DestinatariosPromesaRiesgo filtrados = AvisadorPromesaRiesgo.AplicarPreferencias(d, u => u == "Laura" ? "Nada" : "Todo");
            CollectionAssert.AreEqual(new[] { "tiendaonline@nuevavision.es", "sindueño@nuevavision.es" }, filtrados.Correos);
            filtrados = AvisadorPromesaRiesgo.AplicarPreferencias(d, u => "Nada");
            CollectionAssert.AreEqual(new[] { "sindueño@nuevavision.es" }, filtrados.Correos);
        }

        [TestMethod]
        public async Task Avisador_LeeLaPreferenciaUnaVezPorUsuario_YSiFallaTodo()
        {
            ILectorComercialesPedido lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer("1", PEDIDO)).Returns(new ComercialesPedido
            {
                Pedido = PEDIDO,
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "IF", Correo = "javier@nuevavision.es", Usuarios = new List<string> { "Javier" } } }
            });
            IServicioVendedores vendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => vendedores.JefeEquipo("1", "IF")).Returns(Task.FromResult<VendedorDTO>(null));
            var lecturas = new List<string>();
            var real = new AvisadorPromesaRiesgo(lector, vendedores, A.Fake<IServicioNotificacionesPush>(), A.Fake<NestoAPI.Infraestructure.IServicioCorreoElectronico>(),
                () => new List<string> { "Carlos", "Manuel" }, u => u == "Carlos" ? "carlosadrian@nuevavision.es" : null, _ => { },
                preferenciaDeUsuario: u => { lecturas.Add(u); if (u == "Manuel") throw new InvalidOperationException("sin BD"); return u == "Carlos" ? "SoloCorreo" : null; });

            DestinatariosPromesaRiesgo d = real.Destinatarios(new RiesgoPromesa { Empresa = "1", Pedido = PEDIDO, FechaPrometida = PROMETIDA, FechaNueva = VIERNES });
            DestinatariosPromesaRiesgo otraVez = real.Destinatarios(new RiesgoPromesa { Empresa = "1", Pedido = PEDIDO + 1, FechaPrometida = PROMETIDA, FechaNueva = VIERNES });

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Javier", "NUEVAVISION\\Manuel" }, d.Usuarios, "Carlos solo correo; Manuel (falló la lectura) Todo");
            CollectionAssert.AreEqual(new[] { "javier@nuevavision.es", "carlosadrian@nuevavision.es" }, d.Correos);
            Assert.AreEqual(3, lecturas.Distinct().Count(), "Javier, Carlos y Manuel");
            Assert.AreEqual(3, lecturas.Count, "una lectura por usuario y pasada, aunque haya dos pedidos");
            await Task.CompletedTask;
        }

        [TestMethod]
        public void Controlador_Avisos_LeeYGuardaLaPreferenciaDelUsuario()
        {
            (PedidosVentaPromesaRiesgoController controlador, _, _) = Controlador(Usuario("Carlos"));
            var guardado = new Dictionary<string, string>();
            controlador.LeerPreferencia = u => guardado.TryGetValue(u, out string v) ? v : null;
            controlador.GuardarPreferencia = (u, v) => guardado[u] = PreferenciaAvisosPromesaRiesgo.Normalizar(v);

            var leido = (OkNegotiatedContentResult<AvisosPromesaRiesgoDTO>)controlador.GetAvisos();
            Assert.AreEqual("Todo", leido.Content.Valor);
            Assert.AreEqual(4, leido.Content.Opciones.Count);

            var cambiado = (OkNegotiatedContentResult<AvisosPromesaRiesgoDTO>)controlador.PutAvisos(new AvisosPromesaRiesgoDTO { Valor = "solocorreo" });
            Assert.AreEqual("SoloCorreo", cambiado.Content.Valor);
            Assert.AreEqual("Solo el correo resumen", cambiado.Content.Texto);
            Assert.AreEqual("SoloCorreo", guardado[controlador.User.Identity.Name]);
            Assert.AreEqual("SoloCorreo", ((OkNegotiatedContentResult<AvisosPromesaRiesgoDTO>)controlador.GetAvisos()).Content.Valor);

            Assert.IsInstanceOfType(controlador.PutAvisos(new AvisosPromesaRiesgoDTO { Valor = "Siempre" }), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(controlador.PutAvisos(null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Destinatarios_VendedorGeneralNV_AvisaAQuienGraboElPedido()
        {
            // NestoAPI#708: con el vendedor general (NV) no hay comercial real: campana y correo a quien grabó el pedido.
            var comerciales = new ComercialesPedido
            {
                Pedido = PEDIDO,
                UsuarioPedido = "Laura",
                CorreoUsuarioPedido = "Laura@NuevaVision.es",
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL, Correo = "general@nuevavision.es", Usuarios = new List<string> { "General" } }
                }
            };

            DestinatariosPromesaRiesgo d = AvisadorPromesaRiesgo.Componer(comerciales, new List<ComercialesPedido>(), new[] { "Carlos", "Manuel" },
                new[] { "carlosadrian@nuevavision.es", "manuelrodriguez@nuevavision.es" });

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Laura", "NUEVAVISION\\Carlos", "NUEVAVISION\\Manuel" }, d.Usuarios);
            CollectionAssert.AreEqual(new[] { "laura@nuevavision.es", "carlosadrian@nuevavision.es", "manuelrodriguez@nuevavision.es" }, d.Correos);
        }

        [TestMethod]
        public void Destinatarios_MixtoNVEnEsteticaEIFEnPeluqueria_IFQuienLoGraboSuJefeYDireccion()
        {
            // NestoAPI#708: pedido con NV en la cabecera (estética) e IF en peluquería (VendedoresPedidosGruposProductos):
            // IF por lo de peluquería, quien lo grabó por lo de estética, el jefe de equipo de IF y Dirección.
            List<string> codigos = ComercialesPedido.CodigosVendedores(new[] { "COS", "PEL " }, Constantes.Vendedores.VENDEDOR_GENERAL, "IF ",
                peluqueriaSinVendedorVaAlDeEstetica: true);
            CollectionAssert.AreEqual(new[] { "IF", Constantes.Vendedores.VENDEDOR_GENERAL }, codigos);
            var comerciales = new ComercialesPedido
            {
                Pedido = PEDIDO,
                UsuarioPedido = "Laura",
                CorreoUsuarioPedido = "laura@nuevavision.es",
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "IF", EsPeluqueria = true, Correo = "israel@nuevavision.es", Usuarios = new List<string> { "Israel" } },
                    new ComercialPedido { Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL, Correo = "general@nuevavision.es", Usuarios = new List<string> { "General" } }
                }
            };
            var jefe = new ComercialesPedido
            {
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "ASH", Correo = "alberto@nuevavision.es", Usuarios = new List<string> { "Alberto" } }
                }
            };

            DestinatariosPromesaRiesgo d = AvisadorPromesaRiesgo.Componer(comerciales, new[] { jefe }, new[] { "Carlos", "Manuel" },
                new[] { "carlosadrian@nuevavision.es", "manuelrodriguez@nuevavision.es" });

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Israel", "NUEVAVISION\\Laura", "NUEVAVISION\\Alberto", "NUEVAVISION\\Carlos", "NUEVAVISION\\Manuel" },
                d.Usuarios);
            CollectionAssert.AreEqual(new[] { "israel@nuevavision.es", "laura@nuevavision.es", "alberto@nuevavision.es", "carlosadrian@nuevavision.es",
                "manuelrodriguez@nuevavision.es" }, d.Correos);
        }

        [TestMethod]
        public void Destinatarios_MixtoNVEIF_SoloSeBuscaElJefeDelComercialReal()
        {
            ILectorComercialesPedido lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer("1", PEDIDO)).Returns(new ComercialesPedido
            {
                Pedido = PEDIDO,
                UsuarioPedido = "Laura",
                CorreoUsuarioPedido = "laura@nuevavision.es",
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "IF", EsPeluqueria = true, Correo = "israel@nuevavision.es", Usuarios = new List<string> { "Israel" } },
                    new ComercialPedido { Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL }
                }
            });
            A.CallTo(() => lector.LeerDeVendedor("1", "ASH", null)).Returns(new ComercialesPedido
            {
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "ASH", Correo = "alberto@nuevavision.es", Usuarios = new List<string> { "Alberto" } } }
            });
            IServicioVendedores vendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => vendedores.JefeEquipo("1", "IF")).Returns(Task.FromResult(new VendedorDTO { vendedor = "ASH" }));
            var real = new AvisadorPromesaRiesgo(lector, vendedores, A.Fake<IServicioNotificacionesPush>(), A.Fake<NestoAPI.Infraestructure.IServicioCorreoElectronico>(),
                () => new List<string> { "Carlos", "Manuel" }, u => u == "Carlos" ? "carlosadrian@nuevavision.es" : null, _ => { });

            DestinatariosPromesaRiesgo d = real.Destinatarios(new RiesgoPromesa { Empresa = "1", Pedido = PEDIDO, FechaPrometida = PROMETIDA, FechaNueva = VIERNES });

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Israel", "NUEVAVISION\\Laura", "NUEVAVISION\\Alberto", "NUEVAVISION\\Carlos", "NUEVAVISION\\Manuel" },
                d.Usuarios);
            CollectionAssert.AreEqual(new[] { "israel@nuevavision.es", "laura@nuevavision.es", "alberto@nuevavision.es", "carlosadrian@nuevavision.es" }, d.Correos);
            A.CallTo(() => vendedores.JefeEquipo(A<string>._, Constantes.Vendedores.VENDEDOR_GENERAL)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AvisarCampana_LlegaATodosLosDestinatariosALaCampanaDeNesto()
        {
            ILectorComercialesPedido lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer("1", PEDIDO)).Returns(new ComercialesPedido
            {
                Pedido = PEDIDO,
                Cliente = "40353",
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "JGP", Usuarios = new List<string> { "Javier" } } }
            });
            A.CallTo(() => lector.LeerDeVendedor("1", "ASH", null)).Returns(new ComercialesPedido
            {
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "ASH", Usuarios = new List<string> { "Alberto" } } }
            });
            IServicioVendedores vendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => vendedores.JefeEquipo("1", "JGP")).Returns(Task.FromResult(new VendedorDTO { vendedor = "ASH" }));
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var real = new AvisadorPromesaRiesgo(lector, vendedores, push, A.Fake<NestoAPI.Infraestructure.IServicioCorreoElectronico>(),
                () => new List<string> { "Carlos", "Manuel" }, _ => null, _ => { });

            AvisoPromesaRiesgo aviso = real.Preparar(new RiesgoPromesa { Empresa = "1", Pedido = PEDIDO, FechaPrometida = PROMETIDA, FechaNueva = VIERNES, Motivo = "x" }, false);
            int avisados = await real.AvisarCampana(aviso);

            Assert.AreEqual(4, avisados);
            foreach (string usuario in new[] { "NUEVAVISION\\Javier", "NUEVAVISION\\Alberto", "NUEVAVISION\\Carlos", "NUEVAVISION\\Manuel" })
            {
                A.CallTo(() => push.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO,
                    A<NotificacionPushDTO>.That.Matches(n => n.Tipo == TextosPromesaRiesgo.TIPO_NOTIFICACION))).MustHaveHappenedOnceExactly();
            }
            A.CallTo(() => push.EnviarAVendedor(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------------ eventos (la puerta)

        [TestMethod]
        public void Anotar_Apagado_NoProgramaNada()
        {
            var programados = new List<EventoPromesaRiesgo>();
            EventosPromesaRiesgo.LeerModo = () => ModoAvisoPromesaRiesgo.Apagado;
            EventosPromesaRiesgo.Programar = (e, t) => programados.Add(e);

            EventosPromesaRiesgo.Anotar("1", new[] { "42485" }, "movimiento");

            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void Anotar_Encendido_ProgramaUnJobUnMinutoDespues()
        {
            var programados = new List<(EventoPromesaRiesgo, TimeSpan)>();
            EventosPromesaRiesgo.LeerModo = () => ModoAvisoPromesaRiesgo.Sombra;
            EventosPromesaRiesgo.Programar = (e, t) => programados.Add((e, t));

            EventosPromesaRiesgo.Anotar("1  ", new[] { " 42485 ", "42485", "" }, "movimiento");

            Assert.AreEqual(1, programados.Count);
            Assert.AreEqual("1", programados[0].Item1.Empresa);
            CollectionAssert.AreEqual(new[] { "42485" }, programados[0].Item1.Productos);
            Assert.AreEqual(EventosPromesaRiesgo.RETRASO, programados[0].Item2);
        }

        [TestMethod]
        public void Anotar_SiFallaAlProgramar_NoRompeAQuienLlama()
        {
            EventosPromesaRiesgo.LeerModo = () => ModoAvisoPromesaRiesgo.Activo;
            EventosPromesaRiesgo.Programar = (e, t) => throw new InvalidOperationException("Hangfire caído");

            EventosPromesaRiesgo.Anotar("1", new[] { "42485" }, "movimiento");
        }

        [TestMethod]
        public void TerminarReposicionDeReinaAAlgete_UnSoloEventoConLaCausaDeLaTienda()
        {
            var programados = new List<EventoPromesaRiesgo>();
            EventosPromesaRiesgo.LeerModo = () => ModoAvisoPromesaRiesgo.Activo;
            EventosPromesaRiesgo.Programar = (e, t) => programados.Add(e);

            using (EventosPromesaRiesgo.Ambito())
            {
                // Lo que anota la contabilización del diario (ServicioExtractoProducto) dentro de la transacción…
                EventosPromesaRiesgo.Anotar("1", new[] { "42485", "17404" }, "movimiento de stock (diario _RepoREI)");
                // …y lo que sabe Terminar de la reposición
                ServicioPreparacionReposicion.AnotarPromesaRiesgo("1", "REI", "ALG", 81094, new[]
                {
                    new FilaReposicionEnPreparacion { Producto = "42485", Cantidad = 1 },
                    new FilaReposicionEnPreparacion { Producto = "17404", Cantidad = 2 }
                });
                Assert.AreEqual(0, programados.Count, "dentro del ámbito no se programa nada");
            }

            EventoPromesaRiesgo evento = programados.Single();
            CollectionAssert.AreEquivalent(new[] { "42485", "17404" }, evento.Productos);
            CausaPromesaRiesgo causa = evento.Causas["42485"];
            Assert.AreEqual("REI", causa.Tienda);
            Assert.AreEqual(1, causa.Mandadas);
            Assert.AreEqual(81094, causa.Traspaso);
            StringAssert.Contains(evento.Descripcion, "reposición 81094 de Reina a Algete");
        }

        [TestMethod]
        public void TerminarReposicionDeAlgeteATienda_SinCausaDeTienda()
        {
            var programados = new List<EventoPromesaRiesgo>();
            EventosPromesaRiesgo.LeerModo = () => ModoAvisoPromesaRiesgo.Activo;
            EventosPromesaRiesgo.Programar = (e, t) => programados.Add(e);

            ServicioPreparacionReposicion.AnotarPromesaRiesgo("1", "ALG", "REI", 81100, new[] { new FilaReposicionEnPreparacion { Producto = "42485", Cantidad = 5 } });

            Assert.AreEqual(0, programados.Single().Causas.Count);
        }

        [TestMethod]
        public async Task PedidoGrabado_AnotaSusProductosComoEvento()
        {
            IRepositorioFechaEntregaAgencia repoFechas = A.Fake<IRepositorioFechaEntregaAgencia>();
            A.CallTo(() => repoFechas.LeerPedido("1", 929104)).Returns(Task.FromResult(new PedidoFechaEntregaAgencia
            {
                Empresa = "1",
                Numero = 929104,
                Ruta = "FW",
                ModoServicio = 1,
                ServirJunto = true,
                Lineas = new List<LineaPedidoFechaEntregaAgencia>
                {
                    new LineaPedidoFechaEntregaAgencia { Producto = "42485", Almacen = "ALG", Cantidad = 1, BaseImponible = 10 }
                }
            }));
            A.CallTo(() => repoFechas.LeerCalendario(A<string>._)).Returns(Task.FromResult(new List<ReposicionCalendario>()));
            A.CallTo(() => repoFechas.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            A.CallTo(() => repoFechas.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(new DatosSombraModoServicio());
            var anotados = new List<(string empresa, List<string> productos, IDictionary<string, CausaPromesaRiesgo> causas)>();
            var servicio = new ServicioFechaEntregaAgencia(repoFechas, () => AHORA, (d, a) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday,
                _ => { }, null, (e, p, d, c) => anotados.Add((e, p.ToList(), c)), RepartoStockFalso.DesdeSombra(repoFechas));

            _ = await servicio.CalcularPedidoParaCorreo("1", 929104);

            Assert.AreEqual(1, anotados.Count);
            CollectionAssert.AreEqual(new[] { "42485" }, anotados[0].productos);
            Assert.AreEqual(929104, anotados[0].causas["42485"].PedidoOrigen);
        }

        // ------------------------------------------------------------------ pedidos a proveedor (Carlos, 22/10)

        private static LineaCompraPromesaRiesgo Compra(int id, string producto, int cantidad, DateTime? fecha, bool enviado = true, short estado = -1) =>
            new LineaCompraPromesaRiesgo { Id = id, Producto = producto, Cantidad = cantidad, FechaRecepcion = fecha, Enviado = enviado, Estado = estado };

        [TestMethod]
        public void Compra_SeRetrasaLaFecha_Empeora()
        {
            Dictionary<string, CausaPromesaRiesgo> causas = CambiosCompraPromesaRiesgo.QueEmpeora(4521,
                new[] { Compra(1, "42485", 10, PROMETIDA) }, new[] { Compra(1, "42485", 10, new DateTime(2026, 10, 30)) });

            Assert.AreEqual("el pedido a proveedor 4521 retrasa la fecha prevista del 22/10/2026 al 30/10/2026", causas["42485"].Texto);
        }

        [TestMethod]
        public void Compra_BajaLaCantidadOSeQuitaLaLinea_Empeora()
        {
            Dictionary<string, CausaPromesaRiesgo> causas = CambiosCompraPromesaRiesgo.QueEmpeora(4521,
                new[] { Compra(1, "42485", 10, PROMETIDA), Compra(2, "17404", 5, PROMETIDA) },
                new[] { Compra(1, "42485", 4, PROMETIDA) });

            StringAssert.Contains(causas["42485"].Texto, "baja de 10 a 4");
            StringAssert.Contains(causas["17404"].Texto, "se ha quitado del pedido a proveedor 4521");
        }

        [TestMethod]
        public void Compra_LoQueMejoraONoContaba_NoSeAnota()
        {
            Dictionary<string, CausaPromesaRiesgo> causas = CambiosCompraPromesaRiesgo.QueEmpeora(4521,
                new[] { Compra(1, "42485", 10, VIERNES), Compra(2, "17404", 5, PROMETIDA, enviado: false) },
                new[] { Compra(1, "42485", 12, PROMETIDA), Compra(2, "17404", 5, new DateTime(2026, 11, 5)) });

            Assert.AreEqual(0, causas.Count);
        }

        // ------------------------------------------------------------------ endpoint del corte 2

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static List<RiesgoPromesaListado> Listado() => new List<RiesgoPromesaListado>
        {
            new RiesgoPromesaListado { Id = 1, Empresa = "1", Pedido = 928908, Vendedor = "JGP", FechaPrometida = PROMETIDA, FechaNueva = VIERNES },
            new RiesgoPromesaListado { Id = 2, Empresa = "1", Pedido = 929000, Vendedor = "ASH", FechaPrometida = PROMETIDA },
            new RiesgoPromesaListado { Id = 3, Empresa = "1", Pedido = 929001, Vendedor = "NV", VendedorPeluqueria = "JGP", FechaPrometida = PROMETIDA },
            new RiesgoPromesaListado { Id = 4, Empresa = "1", Pedido = 929002, Vendedor = "OTR", FechaPrometida = PROMETIDA }
        };

        private (PedidosVentaPromesaRiesgoController controlador, IServicioVendedoresVisibles visibles, IServicioVendedores vendedores) Controlador(IPrincipal usuario)
        {
            IRepositorioPromesaRiesgo repo = A.Fake<IRepositorioPromesaRiesgo>();
            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.LeerListado("1", false)).Returns(Task.FromResult(Listado()));
            A.CallTo(() => repo.LeerListado("1", true)).Returns(Task.FromResult(Listado().Concat(new[]
            {
                new RiesgoPromesaListado { Id = 5, Empresa = "1", Pedido = 928000, Vendedor = "JGP", FechaPrometida = PROMETIDA.AddDays(-7), FechaNueva = PROMETIDA.AddDays(-5),
                    Resuelto = PROMETIDA.AddDays(-4), MotivoResolucion = "Se ha servido." }
            }).ToList()));
            IServicioVendedoresVisibles visibles = A.Fake<IServicioVendedoresVisibles>();
            IServicioVendedores vendedores = A.Fake<IServicioVendedores>();
            var controlador = new PedidosVentaPromesaRiesgoController(repo, visibles, vendedores)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario
            };
            return (controlador, visibles, vendedores);
        }

        [TestMethod]
        public async Task Endpoint_DireccionSinVendedor_VeTodos()
        {
            var (controlador, visibles, _) = Controlador(Usuario("Carlos", "Dirección"));

            var ok = await controlador.GetPromesaEnRiesgo() as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;

            Assert.AreEqual(4, ok.Content.Count);
            A.CallTo(() => visibles.LeerVisibles(A<IPrincipal>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, ok.Content[0].DiasRetraso);
            Assert.IsTrue(ok.Content[1].SinFecha);
        }

        [TestMethod]
        public async Task Endpoint_JefeConVendedor_VeLosDeSuEquipo()
        {
            IPrincipal alberto = Usuario("Alberto");
            var (controlador, visibles, vendedores) = Controlador(alberto);
            A.CallTo(() => visibles.PuedeVer(alberto, "1", "ASH")).Returns(Task.FromResult(true));
            A.CallTo(() => vendedores.VendedoresEquipoString("1", "ASH")).Returns(Task.FromResult(new List<string> { "JGP", "ASH" }));

            var ok = await controlador.GetPromesaEnRiesgo("1", "ASH") as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;

            CollectionAssert.AreEqual(new[] { 928908, 929000, 929001 }, ok.Content.Select(p => p.Pedido).ToArray());
        }

        [TestMethod]
        public async Task Endpoint_VendedorQueNoPuedeVer_403()
        {
            IPrincipal javier = Usuario("Javier");
            var (controlador, visibles, _) = Controlador(javier);
            A.CallTo(() => visibles.PuedeVer(javier, "1", "ASH")).Returns(Task.FromResult(false));

            var respuesta = await controlador.GetPromesaEnRiesgo("1", "ASH") as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
        }

        [TestMethod]
        public async Task Endpoint_SinVendedorNiDireccion_LosDeSuVendedorYEquipo()
        {
            IPrincipal javier = Usuario("Javier");
            var (controlador, visibles, _) = Controlador(javier);
            A.CallTo(() => visibles.LeerVisibles(javier, "1")).Returns(Task.FromResult(new List<VendedorVisibleDTO> { new VendedorVisibleDTO { Vendedor = "JGP" } }));

            var ok = await controlador.GetPromesaEnRiesgo() as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;

            CollectionAssert.AreEqual(new[] { 928908, 929001 }, ok.Content.Select(p => p.Pedido).ToArray());
        }

        [TestMethod]
        public async Task Endpoint_ConResueltos_LosDevuelveDetrasConSuResolucion_YSinResueltosNoVienen()
        {
            // Nesto#546: el filtro «Resueltos» de la pantalla pide resueltos=true; por defecto siguen sin venir
            var (controlador, _, _) = Controlador(Usuario("Carlos", "Dirección"));

            var conResueltos = await controlador.GetPromesaEnRiesgo("1", null, true) as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;
            var sinResueltos = await controlador.GetPromesaEnRiesgo() as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;

            Assert.AreEqual(5, conResueltos.Content.Count);
            PedidoPromesaRiesgoDTO resuelto = conResueltos.Content.Last();
            Assert.AreEqual(928000, resuelto.Pedido);
            Assert.IsTrue(resuelto.EstaResuelto);
            Assert.AreEqual("Se ha servido.", resuelto.MotivoResolucion);
            Assert.IsTrue(conResueltos.Content.Take(4).All(p => !p.EstaResuelto));
            Assert.AreEqual(4, sinResueltos.Content.Count);
            Assert.IsFalse(sinResueltos.Content.Any(p => p.EstaResuelto));
        }

        [TestMethod]
        public async Task Endpoint_ConResueltos_ElVendedorSoloVeLosSuyos()
        {
            IPrincipal javier = Usuario("Javier");
            var (controlador, visibles, _) = Controlador(javier);
            A.CallTo(() => visibles.LeerVisibles(javier, "1")).Returns(Task.FromResult(new List<VendedorVisibleDTO> { new VendedorVisibleDTO { Vendedor = "ASH" } }));

            var ok = await controlador.GetPromesaEnRiesgo("1", null, true) as OkNegotiatedContentResult<List<PedidoPromesaRiesgoDTO>>;

            CollectionAssert.AreEqual(new[] { 929000 }, ok.Content.Select(p => p.Pedido).ToArray());
        }

        // ------------------------------------------------------------------ interruptor

        [TestMethod]
        public void Interruptor_ApagadoPorDefecto_YSombraYActivo()
        {
            Assert.AreEqual(ModoAvisoPromesaRiesgo.Apagado, InterruptorAvisoPromesaRiesgo.Interpretar(null));
            Assert.AreEqual(ModoAvisoPromesaRiesgo.Apagado, InterruptorAvisoPromesaRiesgo.Interpretar("true"));
            Assert.AreEqual(ModoAvisoPromesaRiesgo.Sombra, InterruptorAvisoPromesaRiesgo.Interpretar(" sombra "));
            Assert.AreEqual(ModoAvisoPromesaRiesgo.Activo, InterruptorAvisoPromesaRiesgo.Interpretar("Activo"));
            InterruptorAvisoPromesaRiesgo.LeerAjuste = _ => null;
            CollectionAssert.AreEqual(new[] { "Carlos", "Manuel" }, InterruptorAvisoPromesaRiesgo.UsuariosDireccion());
        }
    }
}
