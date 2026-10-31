using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#772: el seguimiento de una reposición por traspaso y por bulto, y «Recogidos 6 de 7 bultos por Andre» en GET
    /// Puntual/Estado (lo que enseña Nesto en vez del botón «El repartidor ya se la ha llevado»).
    /// </summary>
    [TestClass]
    public class SeguimientoReposicionesTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 31);
        private RepositorioFalso repositorio;
        private ServicioSeguimientoReposiciones servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioFalso();
            servicio = new ServicioSeguimientoReposiciones(repositorio, () => HOY.AddHours(16));
        }

        private static MovimientoBultoReparto Mov(int traspaso, int bulto, string tipo, string usuario, DateTime fecha, string origen = OrigenesMovimientoBulto.REPARTIDOR, Guid? clave = null)
            => new MovimientoBultoReparto
            {
                Codigo = $"R{traspaso}-{bulto}", TipoCodigo = "R", NumTraspaso = traspaso, Bulto = bulto, Total = 2, Tipo = tipo, Usuario = usuario,
                FechaHora = fecha, Origen = origen, ClaveIdempotencia = clave ?? Guid.NewGuid()
            };

        [TestMethod]
        public async Task Seguimiento_HistoriaDelTraspasoYDeCadaBulto()
        {
            repositorio.Cabecera = new CabeceraSeguimientoReposicion
            {
                Origen = "REI", Destino = "ALG", Herramienta = "Automatico", UsuarioCreacion = "NestoAPI", FechaCreacion = HOY.AddHours(9),
                UsuarioPreparacion = @"NUEVAVISION\Paloma", FechaPreparada = HOY.AddHours(10), FechaRecogida = HOY.AddHours(12), UsuarioRecogida = @"NUEVAVISION\Andre",
                FechaRecibida = HOY.AddHours(18), UsuarioRecepcion = "Alfredo"
            };
            repositorio.Bultos.Add(new FilaBultoSeguimiento { Codigo = "R81010-1", Bulto = 1, Total = 2, FechaCreacion = HOY.AddHours(10), UsuarioCreacion = "Paloma", FechaLectura = HOY.AddHours(18), UsuarioLectura = "Alfredo" });
            repositorio.Bultos.Add(new FilaBultoSeguimiento { Codigo = "R81010-2", Bulto = 2, Total = 2, FechaCreacion = HOY.AddHours(10), UsuarioCreacion = "Paloma" });
            repositorio.Movimientos.Add(Mov(81010, 1, TiposMovimientoBulto.RECOGIDO, "Andre", HOY.AddHours(12)));
            repositorio.Movimientos.Add(Mov(81010, 1, TiposMovimientoBulto.DESCARGADO, "Andre", HOY.AddHours(17)));
            repositorio.Movimientos.Add(Mov(81010, 2, TiposMovimientoBulto.RECOGIDO, @"NUEVAVISION\Andre", HOY.AddHours(12)));

            SeguimientoReposicionDTO dto = await servicio.Seguimiento("1", 81010);

            Assert.AreEqual("REI", dto.Origen);
            CollectionAssert.AreEqual(new[] { "Creada", "Preparada", "Recogida", "Recibida" }, dto.Eventos.Select(e => e.Tipo).ToList());
            Assert.AreEqual("Paloma", dto.Eventos[1].Usuario, "Sin dominio");
            Assert.AreEqual(2, dto.Bultos.Count);
            BultoSeguimientoReposicionDTO uno = dto.Bultos[0];
            CollectionAssert.AreEqual(new[] { "Etiquetado", "Recogido", "Descargado", "Recibido" }, uno.Eventos.Select(e => e.Tipo).ToList());
            Assert.AreEqual("Recibido en Algete", uno.Estado);
            Assert.AreEqual("Recogido en Reina", uno.Eventos[1].Detalle);
            Assert.AreEqual("En la furgoneta de Andre", dto.Bultos[1].Estado);
        }

        [TestMethod]
        public async Task Seguimiento_CargasDeAntesDelScript_YLaEntregaDeLaParada()
        {
            Guid conMovimiento = Guid.NewGuid();
            repositorio.Cabecera = new CabeceraSeguimientoReposicion { Origen = "ALG", Destino = "ALC" };
            repositorio.Cargas.Add(new CargaAntiguaSeguimiento { Bulto = 1, Usuario = "Miguel", FechaHora = HOY.AddHours(8), ClaveIdempotencia = Guid.NewGuid(), Destino = "ALC", FechaEntrega = HOY.AddHours(11), UsuarioEntrega = "Miguel", Receptor = "Rosa" });
            repositorio.Cargas.Add(new CargaAntiguaSeguimiento { Bulto = 2, Usuario = "Miguel", FechaHora = HOY.AddHours(8), ClaveIdempotencia = conMovimiento, Destino = "ALC" });
            repositorio.Movimientos.Add(Mov(81020, 2, TiposMovimientoBulto.CARGADO, "Miguel", HOY.AddHours(8), clave: conMovimiento));

            SeguimientoReposicionDTO dto = await servicio.Seguimiento("1", 81020);

            CollectionAssert.AreEqual(new[] { "Cargado", "Entregado" }, dto.Bultos[0].Eventos.Select(e => e.Tipo).ToList());
            StringAssert.Contains(dto.Bultos[0].Eventos[1].Detalle, "lo recogió Rosa");
            Assert.AreEqual("Entregado en Alcobendas, sin recibir", dto.Bultos[0].Estado);
            Assert.AreEqual(1, dto.Bultos[1].Eventos.Count(e => e.Tipo == "Cargado"), "La carga con movimiento no sale dos veces");
        }

        [TestMethod]
        public async Task Seguimiento_LoQueNoSeConoce_Null_YElControlador404()
        {
            Assert.IsNull(await servicio.Seguimiento("1", 1));

            var controlador = new ReposicionesController(null, A.Fake<IServicioPreparacionReposicion>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Seguimiento = servicio
            };
            HttpResponseMessage respuesta = await (await controlador.GetSeguimiento(1)).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "No se sabe nada de la reposición 1.");
        }

        [TestMethod]
        public async Task CompletarRecogida_SeisDeSiete_YQuienElUltimo()
        {
            repositorio.Traspasos.AddRange(new[] { 81010, 81011 });
            for (short b = 1; b <= 5; b++)
            {
                repositorio.BultosDe.Add(new FilaBultoReposicion { Traspaso = 81010, Bulto = b, Total = 5 });
            }
            repositorio.BultosDe.Add(new FilaBultoReposicion { Traspaso = 81011, Bulto = 1, Total = 2 });
            repositorio.BultosDe.Add(new FilaBultoReposicion { Traspaso = 81011, Bulto = 2, Total = 2 });
            for (int b = 1; b <= 5; b++)
            {
                repositorio.Movimientos.Add(Mov(81010, b, TiposMovimientoBulto.RECOGIDO, "Miguel", HOY.AddHours(11)));
            }
            repositorio.Movimientos.Add(Mov(81011, 1, TiposMovimientoBulto.RECOGIDO, @"NUEVAVISION\Andre", HOY.AddHours(12)));
            repositorio.Movimientos.Add(Mov(81011, 1, TiposMovimientoBulto.DESCARGADO, @"NUEVAVISION\Andre", HOY.AddHours(17)));
            var estado = new EstadoReposicionPuntualDTO { Origen = "REI", Destino = "ALG", CierreHoy = HOY.AddHours(10) };

            await servicio.CompletarRecogida(estado, "1");

            Assert.AreEqual(7, estado.BultosTotal);
            Assert.AreEqual(6, estado.BultosRecogidos);
            Assert.AreEqual("Andre", estado.RepartidorRecogida);
            Assert.AreEqual(HOY.AddHours(12), estado.FechaUltimoBultoRecogido);
            Assert.AreEqual(HOY, repositorio.DiaCortePedido);
        }

        [TestMethod]
        public async Task CompletarRecogida_SinFurgoneta_NoTocaNada_YSiFallaNoLanza()
        {
            repositorio.Furgoneta = false;
            var estado = new EstadoReposicionPuntualDTO { Origen = "REI" };
            await servicio.CompletarRecogida(estado, "1");
            Assert.IsNull(estado.BultosTotal);

            repositorio.Furgoneta = true;
            repositorio.Fallar = true;
            await servicio.CompletarRecogida(estado, "1");
            Assert.IsNull(estado.BultosTotal);
        }

        private class RepositorioFalso : IRepositorioSeguimientoReposiciones
        {
            public CabeceraSeguimientoReposicion Cabecera;
            public readonly List<FilaBultoSeguimiento> Bultos = new List<FilaBultoSeguimiento>();
            public readonly List<MovimientoBultoReparto> Movimientos = new List<MovimientoBultoReparto>();
            public readonly List<CargaAntiguaSeguimiento> Cargas = new List<CargaAntiguaSeguimiento>();
            public readonly List<int> Traspasos = new List<int>();
            public readonly List<FilaBultoReposicion> BultosDe = new List<FilaBultoReposicion>();
            public bool Furgoneta = true;
            public bool Fallar;
            public DateTime? DiaCortePedido;

            public Task<CabeceraSeguimientoReposicion> LeerCabecera(string empresa, int traspaso) => Task.FromResult(Cabecera);
            public Task<List<FilaBultoSeguimiento>> LeerBultos(string empresa, int traspaso) => Task.FromResult(Bultos.ToList());
            public Task<List<MovimientoBultoReparto>> LeerMovimientos(IReadOnlyCollection<int> traspasos)
                => Task.FromResult(Movimientos.Where(m => traspasos.Contains(m.NumTraspaso ?? 0)).ToList());
            public Task<List<CargaAntiguaSeguimiento>> LeerCargasParadas(string empresa, int traspaso) => Task.FromResult(Cargas.ToList());

            public Task<List<int>> TraspasosDelViaje(string empresa, string origen, string destino, DateTime diaCorte, DateTime hoy)
            {
                if (Fallar)
                {
                    throw new InvalidOperationException("BD caída");
                }
                DiaCortePedido = diaCorte;
                return Task.FromResult(Traspasos.ToList());
            }

            public Task<List<FilaBultoReposicion>> LeerBultosDe(string empresa, IReadOnlyCollection<int> traspasos)
                => Task.FromResult(BultosDe.Where(b => traspasos.Contains(b.Traspaso)).ToList());

            public bool FurgonetaDisponible() => Furgoneta;
        }
    }
}
