using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Una fila de Ubicaciones en memoria.</summary>
    public class FilaUbicacionFalsa
    {
        public int Id { get; set; }
        public string Hueco { get; set; }
        public int Estado { get; set; }
        public int Cantidad { get; set; }
        public int? Linea { get; set; }
        public int? Pedido { get; set; }
        /// <summary>El picking de la línea (las reservas de otros pickings no son «propias»).</summary>
        public int Picking { get; set; }
        public FilaUbicacionFalsa Copia() => (FilaUbicacionFalsa)MemberwiseClone();
    }

    /// <summary>
    /// C1: Ubicaciones, Inventarios y el extracto de UN producto en ALG, en memoria. <see cref="Contabilizar"/> hace lo que
    /// prdContabilizarInventario con las filas del usuario: las de estado 0 del hueco pasan a lo contado, el extracto se mueve en la
    /// diferencia y la fila pasa a contabilizada.
    /// </summary>
    public class TransaccionNoEstaFalsa : TransaccionSalidaFalsa, ITransaccionNoEsta
    {
        public const string PRODUCTO = "17404";
        public bool ConUbicaciones { get; set; } = true;
        public ProductoNoEsta Ficha { get; set; } = new ProductoNoEsta { Numero = PRODUCTO, Descripcion = "CREMA", Ubicar = true };
        public List<FilaUbicacionFalsa> Ubicaciones { get; set; } = new List<FilaUbicacionFalsa>();
        public int Stock { get; set; }
        /// <summary>Filas pendientes del producto de OTROS (alguien lo está inventariando).</summary>
        public int PendientesDeOtros { get; set; }
        public List<Inventario> Inventario { get; set; } = new List<Inventario>();
        public List<Modificacion> Rastros { get; set; } = new List<Modificacion>();
        public List<string> Bloqueos { get; } = new List<string>();
        public Exception FallarAlContabilizarInventario { get; set; }
        public int TraspasoInventario { get; set; } = 7001;
        private int siguienteId = 1000;

        public Task Bloquear(string recurso)
        {
            Bloqueos.Add(recurso);
            return Task.CompletedTask;
        }

        public Task<bool> ControlUbicaciones(string empresa, string almacen) => Task.FromResult(ConUbicaciones);
        public Task<ProductoNoEsta> LeerProducto(string empresa, string producto) => Task.FromResult(Ficha);

        public Task<InventarioPendienteNoEsta> InventarioPendiente(string empresa, string almacen, string producto, string usuarioTecnico) =>
            Task.FromResult(new InventarioPendienteNoEsta
            {
                DelProducto = PendientesDeOtros + Inventario.Count(i => i.Estado == 1),
                DelUsuario = Inventario.Count(i => i.Estado == 1 && i.Usuario == usuarioTecnico)
            });

        public Task<int> LibreEnHueco(string almacen, string producto, string hueco) =>
            Task.FromResult(Ubicaciones.Where(u => u.Estado == 0 && u.Hueco == hueco).Sum(u => u.Cantidad));

        public Task<int> StockAlmacen(string almacen, string producto) => Task.FromResult(Stock);

        public Task InsertarFilaInventario(Inventario fila)
        {
            Inventario.Add(fila);
            return Task.CompletedTask;
        }

        public Task<int> Contabilizar(string empresa, string usuario, string almacen)
        {
            if (FallarAlContabilizarInventario != null)
            {
                throw FallarAlContabilizarInventario;
            }
            foreach (Inventario fila in Inventario.Where(i => i.Estado == 1 && i.Usuario == usuario).ToList())
            {
                string hueco = fila.Pasillo + fila.Fila + fila.Columna;
                Ubicaciones.Where(u => u.Estado == 0 && u.Hueco == hueco).ToList().ForEach(u => u.Cantidad = fila.StockReal ?? 0);
                Ubicaciones.RemoveAll(u => u.Estado >= 0 && u.Cantidad == 0);
                Stock += (fila.StockReal ?? 0) - (fila.StockCalculado ?? 0);
                fila.Estado = 2;
                fila.NºTraspaso = TraspasoInventario;
            }
            return Task.FromResult(1);
        }

        public Task<int?> LeerTraspasoInventario(string empresa, string almacen, string usuario, DateTime fecha) => Task.FromResult((int?)TraspasoInventario);

        public Task<List<HuecoAlternativoDTO>> LibresEnOtrosHuecos(string empresa, string almacen, string producto, string huecoOrigen) =>
            Task.FromResult(Ubicaciones.Where(u => u.Estado == 0 && u.Hueco != huecoOrigen).GroupBy(u => u.Hueco)
                .Where(g => g.Sum(u => u.Cantidad) > 0)
                .Select(g => new HuecoAlternativoDTO { Codigo = g.Key, Ubicacion = $"{g.Key.Substring(0, 3)}/{g.Key.Substring(3, 3)}/{g.Key.Substring(6, 3)}", Cantidad = g.Sum(u => u.Cantidad) })
                .ToList());

        public Task<List<LibreEnHueco>> LibresEnHueco(string empresa, string almacen, string producto, string hueco) =>
            Task.FromResult(Ubicaciones.Where(u => u.Estado == 0 && u.Hueco == hueco && u.Cantidad > 0).Select(u => new LibreEnHueco { Ubicacion = u.Id, Cantidad = u.Cantidad }).ToList());

        public Task<string> AlmacenPicking(string empresa, int picking) => Task.FromResult("ALG");

        public Task<List<ReservaPropiaNoEsta>> ReservasPicking(string empresa, int picking, string producto, string hueco) =>
            Task.FromResult(Ubicaciones.Where(u => u.Estado == 3 && u.Picking == picking && u.Hueco == hueco)
                .OrderByDescending(u => u.Pedido).ThenByDescending(u => u.Linea)
                .Select(u => new ReservaPropiaNoEsta { Ubicacion = u.Id, Linea = u.Linea.Value, Pedido = u.Pedido.Value, EmpresaLinea = "1", Almacen = "ALG", Producto = producto, Cantidad = u.Cantidad })
                .ToList());

        public Task SoltarReservaAlHueco(int ubicacion, int cantidad, string usuario)
        {
            FilaUbicacionFalsa reserva = Ubicaciones.Single(u => u.Id == ubicacion && u.Estado == 3);
            Ubicaciones.Add(new FilaUbicacionFalsa { Id = siguienteId++, Hueco = reserva.Hueco, Estado = 0, Cantidad = cantidad });
            reserva.Cantidad -= cantidad;
            Ubicaciones.RemoveAll(u => u.Cantidad == 0);
            return Task.CompletedTask;
        }

        public Task ReservarDeLaLibre(int libre, int cantidad, int linea, int pedido, string empresaLinea, string usuario)
        {
            FilaUbicacionFalsa fila = Ubicaciones.Single(u => u.Id == libre && u.Estado == 0);
            Ubicaciones.Add(new FilaUbicacionFalsa { Id = siguienteId++, Hueco = fila.Hueco, Estado = 3, Cantidad = cantidad, Linea = linea, Pedido = pedido, Picking = 100070 });
            fila.Cantidad -= cantidad;
            Ubicaciones.RemoveAll(u => u.Cantidad == 0);
            return Task.CompletedTask;
        }

        public Task ApuntarRastro(Modificacion rastro)
        {
            Rastros.Add(rastro);
            return Task.CompletedTask;
        }

        // Para deshacer (transacción que no se confirma o falla)
        internal (List<FilaUbicacionFalsa>, int, List<Inventario>, int, int) Foto() =>
            (Ubicaciones.Select(u => u.Copia()).ToList(), Stock, Inventario.Select(i => new Inventario { Estado = i.Estado, Usuario = i.Usuario, StockCalculado = i.StockCalculado, StockReal = i.StockReal, Pasillo = i.Pasillo, Fila = i.Fila, Columna = i.Columna, NºTraspaso = i.NºTraspaso }).ToList(),
                Rastros.Count, SacadoDePedidos.Count);

        internal void Volver((List<FilaUbicacionFalsa> Ubicaciones, int Stock, List<Inventario> Inventario, int Rastros, int Sacados) foto)
        {
            Ubicaciones = foto.Ubicaciones;
            Stock = foto.Stock;
            Inventario = foto.Inventario;
            Rastros.RemoveRange(foto.Rastros, Rastros.Count - foto.Rastros);
            SacadoDePedidos.RemoveRange(foto.Sacados, SacadoDePedidos.Count - foto.Sacados);
        }
    }

    /// <summary>La transacción en memoria: confirma con la misma regla que la de verdad; si no, vuelve a como estaba.</summary>
    public class RepositorioNoEstaFalso : IRepositorioNoEsta
    {
        public TransaccionNoEstaFalsa Transaccion { get; } = new TransaccionNoEstaFalsa();
        public int Llamadas { get; private set; }
        public bool Confirmada { get; private set; }

        public async Task<ResultadoNoEsta> EnTransaccion(Func<ITransaccionNoEsta, Task<ResultadoNoEsta>> trabajo, bool deshacerSiempre)
        {
            Llamadas++;
            var foto = Transaccion.Foto();
            try
            {
                ResultadoNoEsta resultado = await trabajo(Transaccion).ConfigureAwait(false);
                Confirmada = !deshacerSiempre && resultado?.Estado == EstadoNoEsta.Hecho && resultado.Resultado?.Regularizado == true;
                if (!Confirmada)
                {
                    Transaccion.Volver(foto);
                }
                return resultado;
            }
            catch
            {
                Transaccion.Volver(foto);
                throw;
            }
        }
    }

    /// <summary>
    /// C1 (Carlos 12/11/26): «Es ir hueco a hueco buscando, hasta que en uno haya stock real o hasta que en ninguno quede ya stock
    /// calculado. En cada hueco que marquemos que no hay, se hace inventario del hueco para que no salga en otro picking.»
    /// </summary>
    [TestClass]
    public class NoEstaSalidaTests
    {
        private const string EMPRESA = "1";
        private const string HUECO = "010002003";
        private const string OTRO_HUECO = "011001001";
        private RepositorioNoEstaFalso repositorio;
        private TransaccionNoEstaFalsa tx;
        private IAvisadorFaltasPicking avisador;
        private bool activo;
        private ServicioNoEstaSalida servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioNoEstaFalso();
            tx = repositorio.Transaccion;
            avisador = A.Fake<IAvisadorFaltasPicking>();
            activo = true;
            servicio = new ServicioNoEstaSalida(new IOrigenNoEsta[] { new OrigenNoEstaPicking() }, repositorio, null, avisador, () => activo,
                () => new DateTime(2026, 11, 12, 10, 0, 0));
            // En el hueco de la parada: 2 libres, 1 reservada para la línea 9001 de este picking y 3 de otro picking
            tx.Ubicaciones = new List<FilaUbicacionFalsa>
            {
                new FilaUbicacionFalsa { Id = 1, Hueco = HUECO, Estado = 0, Cantidad = 2 },
                new FilaUbicacionFalsa { Id = 2, Hueco = HUECO, Estado = 3, Cantidad = 1, Linea = 9001, Pedido = 930414, Picking = 100070 },
                new FilaUbicacionFalsa { Id = 3, Hueco = HUECO, Estado = 3, Cantidad = 3, Linea = 8001, Pedido = 930001, Picking = 100060 }
            };
            tx.Stock = 6;
        }

        private static IPrincipal Usuario(params string[] grupos) =>
            new GenericPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Pedro") }, "prueba"), grupos);

        private Task<ResultadoNoEsta> NoEsta(int cantidad = 1, bool ensayo = false, string tipo = "PICK") =>
            servicio.MarcarNoEsta(EMPRESA, tipo, 100070, new NoEstaSalidaDTO { Producto = TransaccionNoEstaFalsa.PRODUCTO, Hueco = "010/002/003", Cantidad = cantidad },
                Usuario(ensayo ? "Dirección" : "Almacén"), ensayo);

        [TestMethod]
        public async Task NoEsta_HuecoConLibreYReservasDeOtros_SoloQuitaLoLibreYLoPropio_YManda_AlSiguienteHueco()
        {
            tx.Ubicaciones.Add(new FilaUbicacionFalsa { Id = 4, Hueco = OTRO_HUECO, Estado = 0, Cantidad = 5 });
            tx.Stock = 11;

            ResultadoNoEsta resultado = await NoEsta();

            Assert.AreEqual(EstadoNoEsta.Hecho, resultado.Estado);
            ResultadoNoEstaDTO dto = resultado.Resultado;
            Assert.IsTrue(dto.Regularizado);
            Assert.AreEqual(3, dto.UnidadesRegularizadas, "Las 2 libres y la propia que no estaba");
            Assert.AreEqual(8, tx.Stock, "El extracto baja justo eso");
            Assert.AreEqual(3, tx.Ubicaciones.Single(u => u.Hueco == HUECO).Cantidad, "Solo queda la reserva del otro picking");
            Assert.AreEqual(8001, tx.Ubicaciones.Single(u => u.Hueco == HUECO).Linea);
            Assert.AreEqual(0, tx.Inventario.Count(i => i.Estado == 1), "Ninguna fila de inventario pendiente");
            Inventario fila = tx.Inventario.Single();
            Assert.AreEqual(ServicioNoEstaSalida.USUARIO_INVENTARIO, fila.Usuario);
            Assert.AreEqual(3, fila.StockCalculado);
            Assert.AreEqual(0, fila.StockReal);
            Assert.AreEqual(("010", "002", "003"), (fila.Pasillo, fila.Fila, fila.Columna));
            Assert.AreEqual(7001, dto.TraspasoInventario);
            FilaUbicacionFalsa nueva = tx.Ubicaciones.Single(u => u.Hueco == OTRO_HUECO && u.Estado == 3);
            Assert.AreEqual((9001, 1), (nueva.Linea.Value, nueva.Cantidad), "La reserva de la línea pasa al siguiente hueco");
            Assert.AreEqual(4, tx.Ubicaciones.Single(u => u.Hueco == OTRO_HUECO && u.Estado == 0).Cantidad);
            Assert.AreEqual("011/001/001", dto.HuecoPropuesto);
            Assert.IsFalse(dto.SinMasHuecos);
            Assert.AreEqual(0, tx.SacadoDePedidos.Count);
            Assert.IsTrue(repositorio.Confirmada);
            CollectionAssert.AreEqual(new[] { "InventarioRellenar:1:ALG", "InventarioRellenar:1:ALG:010:002:003" }, tx.Bloqueos);
            Modificacion rastro = tx.Rastros.Single();
            StringAssert.Contains(rastro.Nuevo, ServicioNoEstaSalida.OPERACION);
            StringAssert.Contains(rastro.Nuevo, "Pedro");
            StringAssert.Contains(rastro.Anterior, "100070");
            StringAssert.Contains(dto.Mensaje, "Vaya a 011/001/001");
            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task NoEsta_SinMasHuecos_EsFaltaDefinitiva_SaleDelPedidoYSeAvisa()
        {
            ResultadoNoEsta resultado = await NoEsta();

            ResultadoNoEstaDTO dto = resultado.Resultado;
            Assert.IsTrue(dto.Regularizado);
            Assert.IsTrue(dto.SinMasHuecos);
            Assert.AreEqual(1, dto.UnidadesEnFaltaDefinitiva);
            Assert.IsNull(dto.HuecoPropuesto);
            Assert.AreEqual((9001, 1), tx.SacadoDePedidos.Single(), "Como al terminar: queda pendiente para otra entrega");
            Assert.AreEqual(3, tx.Stock, "6 - (2 libres + 1 propia)");
            Assert.IsFalse(tx.Ubicaciones.Any(u => u.Estado == 0 || u.Estado == 2), "Ni libre ni «pendiente de ubicar»: no puede salir en otro picking");
            A.CallTo(() => avisador.Avisar(EMPRESA, 100070, "ALG",
                A<IReadOnlyList<FaltaPickingPedido>>.That.Matches(f => f.Single().Pedido == 930414 && f.Single().Unidades == 1))).MustHaveHappenedOnceExactly();
            StringAssert.Contains(dto.Mensaje, "No queda en ningún otro hueco");
        }

        [TestMethod]
        public async Task NoEsta_SiFallaLaContabilizacion_NoSeTocaNada()
        {
            tx.FallarAlContabilizarInventario = new InvalidOperationException("No se han podido contabilizar las líneas de producto");

            ResultadoNoEsta resultado = await NoEsta();

            Assert.AreEqual(EstadoNoEsta.NoSePuede, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "No se ha podido dejar el hueco a cero");
            StringAssert.Contains(resultado.Mensaje, "No se han podido contabilizar las líneas de producto");
            Assert.AreEqual(6, tx.Stock);
            Assert.AreEqual(0, tx.Inventario.Count, "Ninguna fila de inventario (ni pendiente)");
            Assert.AreEqual(1, tx.Ubicaciones.Single(u => u.Linea == 9001).Cantidad, "La reserva sigue donde estaba");
            Assert.AreEqual(2, tx.Ubicaciones.Single(u => u.Estado == 0).Cantidad);
            Assert.AreEqual(0, tx.Rastros.Count);
            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task NoEsta_SiElProductoSeEstaInventariando_NoSeTocaNada()
        {
            tx.PendientesDeOtros = 1;

            ResultadoNoEsta resultado = await NoEsta();

            Assert.AreEqual(EstadoNoEsta.Hecho, resultado.Estado);
            Assert.IsFalse(resultado.Resultado.Regularizado);
            StringAssert.Contains(resultado.Resultado.MotivoSinRegularizar, "se está inventariando");
            Assert.AreEqual(6, tx.Stock);
            Assert.AreEqual(0, tx.Inventario.Count);
            Assert.IsFalse(repositorio.Confirmada);
        }

        [TestMethod]
        public async Task NoEsta_EnUnAlmacenSinUbicaciones_NoCambiaNada()
        {
            tx.ConUbicaciones = false;

            ResultadoNoEsta resultado = await NoEsta();

            Assert.IsFalse(resultado.Resultado.Regularizado);
            Assert.AreEqual(0, tx.Inventario.Count);
            Assert.AreEqual(0, tx.Bloqueos.Count);
        }

        [TestMethod]
        public async Task NoEsta_ConElInterruptorApagado_NoEntraEnLaBaseDeDatos()
        {
            activo = false;

            ResultadoNoEsta resultado = await NoEsta();

            Assert.IsFalse(resultado.Resultado.Regularizado);
            StringAssert.Contains(resultado.Resultado.MotivoSinRegularizar, ServicioNoEstaSalida.CLAVE);
            Assert.AreEqual(0, repositorio.Llamadas);
        }

        [TestMethod]
        public async Task NoEsta_EnUnaReposicion_TodaviaNo_DaLaFaltaComoSiempre()
        {
            ResultadoNoEsta resultado = await NoEsta(tipo: "REPO");

            Assert.AreEqual(EstadoNoEsta.Hecho, resultado.Estado);
            Assert.IsFalse(resultado.Resultado.Regularizado);
            Assert.AreEqual(0, repositorio.Llamadas);
        }

        [TestMethod]
        public async Task NoEsta_SinReservaDeEstaSalidaEnElHueco_NoSeRegulariza()
        {
            tx.Ubicaciones.RemoveAll(u => u.Linea == 9001);

            ResultadoNoEsta resultado = await NoEsta();

            Assert.IsFalse(resultado.Resultado.Regularizado);
            Assert.AreEqual(0, tx.Inventario.Count);
        }

        [TestMethod]
        public async Task NoEsta_Ensayo_HaceLoMismoYNoGuarda()
        {
            ResultadoNoEsta resultado = await NoEsta(ensayo: true);

            Assert.IsTrue(resultado.Resultado.Regularizado);
            Assert.IsTrue(resultado.Resultado.Ensayo);
            Assert.IsFalse(repositorio.Confirmada);
            Assert.AreEqual(6, tx.Stock);
            Assert.AreEqual(0, tx.SacadoDePedidos.Count);
            A.CallTo(() => avisador.Avisar(A<string>._, A<int>._, A<string>._, A<IReadOnlyList<FaltaPickingPedido>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task NoEsta_SinPermiso_403()
        {
            ResultadoNoEsta resultado = await servicio.MarcarNoEsta(EMPRESA, "PICK", 100070,
                new NoEstaSalidaDTO { Producto = TransaccionNoEstaFalsa.PRODUCTO, Hueco = HUECO, Cantidad = 1 }, Usuario("Vendedores"));

            Assert.AreEqual(EstadoNoEsta.SinPermiso, resultado.Estado);
            Assert.AreEqual(0, repositorio.Llamadas);
        }

        [TestMethod]
        public async Task NoEsta_SalidaYaTerminada_NoSePuede()
        {
            await tx.ApuntarTerminada(EMPRESA, "PICK", 100070, "Pedro");

            ResultadoNoEsta resultado = await NoEsta();

            Assert.AreEqual(EstadoNoEsta.NoSePuede, resultado.Estado);
            Assert.AreEqual(6, tx.Stock);
        }

        [TestMethod]
        public async Task NoEsta_MasDeLoReservado_SoloSueltaLoPropioQueHay()
        {
            ResultadoNoEsta resultado = await NoEsta(cantidad: 5);

            Assert.AreEqual(1, tx.SacadoDePedidos.Single().Cantidad, "Solo había 1 reservada para esta salida en el hueco");
            Assert.AreEqual(3, tx.Ubicaciones.Single(u => u.Linea == 8001).Cantidad, "La del otro picking no se toca");
        }

        [TestMethod]
        public void Descuadre_DiceQueFalla()
        {
            Assert.IsNull(ServicioNoEstaSalida.Descuadre(3, 10, new InventarioPendienteNoEsta(), 0, 7));
            StringAssert.Contains(ServicioNoEstaSalida.Descuadre(3, 10, new InventarioPendienteNoEsta { DelUsuario = 1 }, 0, 7), "pendiente");
            StringAssert.Contains(ServicioNoEstaSalida.Descuadre(3, 10, new InventarioPendienteNoEsta(), 1, 7), "libres");
            StringAssert.Contains(ServicioNoEstaSalida.Descuadre(3, 10, new InventarioPendienteNoEsta(), 0, 8), "tenía que bajar 3");
        }

        [TestMethod]
        public void Sql_ElExtractoSoloSeLee_YLaFilaDeInventarioVaPorElInsertDeSiempre()
        {
            foreach (string sql in new[] { TransaccionNoEstaSql.SQL_LIBRE_EN_HUECO, TransaccionNoEstaSql.SQL_STOCK_ALMACEN, TransaccionNoEstaSql.SQL_RESERVA_A_LIBRE,
                TransaccionNoEstaSql.SQL_RESERVAS_PICKING, TransaccionNoEstaSql.SQL_INVENTARIO_PENDIENTE })
            {
                Assert.IsFalse(sql.IndexOf("INSERT INTO ExtractoProducto", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsFalse(sql.IndexOf("UPDATE ExtractoProducto", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsFalse(sql.IndexOf("INSERT INTO Inventarios", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            StringAssert.Contains(TransaccionNoEstaSql.SQL_RESERVA_A_LIBRE, "Estado = 3");
            StringAssert.Contains(TransaccionNoEstaSql.SQL_RESERVAS_PICKING, "ISNULL(l.Recoger, 0) = 0");
        }
    }
}
