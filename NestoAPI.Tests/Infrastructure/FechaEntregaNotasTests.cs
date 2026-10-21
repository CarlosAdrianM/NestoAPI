using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#582: la nota de entrega automática nace sin fecha (31/12/2099) para no colarse en el picking de hoy.
    /// Nesto pregunta la fecha al hacer el albarán desde el detalle: busca las notas sin fecha de ese pedido y les
    /// pone la que diga el usuario.
    /// </summary>
    [TestClass]
    public class FechaEntregaNotasTests
    {
        private NVEntities db;
        private List<CabPedidoVta> cabeceras;
        private List<LinPedidoVta> lineas;
        private GestorFechaEntregaNotas gestor;
        private static readonly DateTime SinFecha = CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR;

        [TestInitialize]
        public void Setup()
        {
            cabeceras = new List<CabPedidoVta>();
            lineas = new List<LinPedidoVta>();
            db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabPedidoVtas).Returns(DbSetCon(cabeceras));
            A.CallTo(() => db.LinPedidoVtas).Returns(DbSetCon(lineas));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => db.SaveChangesAsync(A<CancellationToken>._)).Returns(Task.FromResult(1));
            gestor = new GestorFechaEntregaNotas(db) { Hoy = () => new DateTime(2026, 10, 1) };
        }

        private CabPedidoVta Nota(int numero, int pedidoOrigen, int albaran, DateTime fechaLineas, short estado = 1, int picking = 0)
        {
            var cab = new CabPedidoVta
            {
                Empresa = "1", Número = numero, NotaEntrega = true, PedidoOrigen = pedidoOrigen, AlbaranOrigen = albaran,
                Comentarios = "Nº Serie X\r\nNOTA DE ENTREGA: pendiente de entregar del pedido " + pedidoOrigen + " (albarán " + albaran + ")\r\n" +
                    CreadorNotaEntregaPendiente.AVISO_SIN_FECHA
            };
            cabeceras.Add(cab);
            lineas.Add(new LinPedidoVta { Empresa = "1", Número = numero, Nº_Orden = numero * 10, Fecha_Entrega = fechaLineas, Estado = estado, Picking = picking, YaFacturado = true });
            return cab;
        }

        [TestMethod]
        public async Task NotasSinFecha_DevuelveLasDeEsePedidoQueSiguenSinFecha()
        {
            Nota(927519, 927116, 730321, SinFecha);
            Nota(927600, 927116, 730400, new DateTime(2026, 10, 7)); // ya tiene fecha
            Nota(927700, 999999, 730500, SinFecha);                  // de otro pedido

            List<NotaEntregaSinFechaDTO> notas = await gestor.NotasSinFecha("1", 927116);

            Assert.AreEqual(1, notas.Count);
            Assert.AreEqual(927519, notas[0].Numero);
            Assert.AreEqual(730321, notas[0].Albaran);
        }

        [TestMethod]
        public async Task PonerFecha_CambiaLasLineasPendientesYQuitaElAviso()
        {
            CabPedidoVta nota = Nota(927519, 927116, 730321, SinFecha);

            string error = await gestor.PonerFecha("1", 927519, new DateTime(2026, 10, 6), "NUEVAVISION\\Alfredo");

            Assert.IsNull(error);
            Assert.AreEqual(new DateTime(2026, 10, 6), lineas.Single().Fecha_Entrega);
            Assert.IsFalse(nota.Comentarios.Contains("SIN FECHA"), "El aviso sobra ya");
            StringAssert.Contains(nota.Comentarios, "Nº Serie X");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PonerFecha_EnAlgoQueNoEsNotaDeEntrega_Rechaza()
        {
            CabPedidoVta nota = Nota(927519, 927116, 730321, SinFecha);
            nota.NotaEntrega = false;

            string error = await gestor.PonerFecha("1", 927519, new DateTime(2026, 10, 6), "u");

            StringAssert.Contains(error, "nota de entrega");
            Assert.AreEqual(SinFecha, lineas.Single().Fecha_Entrega);
        }

        [TestMethod]
        public async Task PonerFecha_EnElPasado_Rechaza()
        {
            Nota(927519, 927116, 730321, SinFecha);

            string error = await gestor.PonerFecha("1", 927519, new DateTime(2026, 9, 30), "u");

            StringAssert.Contains(error, "pasado");
        }

        [TestMethod]
        public async Task PonerFecha_NoTocaLineasQueYaTienenPicking()
        {
            Nota(927519, 927116, 730321, SinFecha, picking: 99700);

            string error = await gestor.PonerFecha("1", 927519, new DateTime(2026, 10, 6), "u");

            StringAssert.Contains(error, "picking");
            Assert.AreEqual(SinFecha, lineas.Single().Fecha_Entrega);
        }

        // NestoAPI#688 (decisión 9): la casilla «¿Servir lo pendiente en cuanto haya stock?» se cambia en la nota sin picking

        private NestoAPI.Infraestructure.PedidosVenta.IRepositorioServirPendienteAlHaberStock RepositorioCon(bool columnas = true)
        {
            var repositorio = A.Fake<NestoAPI.Infraestructure.PedidosVenta.IRepositorioServirPendienteAlHaberStock>();
            A.CallTo(() => repositorio.ColumnasExisten()).Returns(columnas);
            gestor.RepositorioServirPendiente = repositorio;
            gestor.FechaEntregaAjustada = (fecha, ruta, almacen) => fecha.AddDays(1);
            return repositorio;
        }

        [TestMethod]
        public async Task CambiarServirAlHaberStock_Marcar_PoneFechaTodoJuntoYGuarda()
        {
            var repositorio = RepositorioCon();
            CabPedidoVta nota = Nota(927519, 927116, 730321, SinFecha);
            nota.ModoServicio = Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO;

            string error = await gestor.CambiarServirAlHaberStock("1", 927519, true, "NUEVAVISION\\Marta");

            Assert.IsNull(error);
            Assert.AreEqual(new DateTime(2026, 10, 2), lineas.Single().Fecha_Entrega, "La fecha mínima de hoy (hora de corte, ruta, festivos)");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, nota.ModoServicio);
            Assert.IsTrue(nota.ServirJunto);
            Assert.IsFalse(nota.Comentarios.Contains(CreadorNotaEntregaPendiente.AVISO_SIN_FECHA));
            StringAssert.Contains(nota.Comentarios, CreadorNotaEntregaPendiente.AVISO_SERVIR_AL_HABER_STOCK);
            StringAssert.StartsWith(nota.Comentarios, "Nº Serie X");
            A.CallTo(() => repositorio.Guardar("1", 927519, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CambiarServirAlHaberStock_Desmarcar_VuelveASinFechaConAviso()
        {
            var repositorio = RepositorioCon();
            CabPedidoVta nota = Nota(927519, 927116, 730321, new DateTime(2026, 10, 2));
            nota.Comentarios = "NOTA DE ENTREGA\r\n" + CreadorNotaEntregaPendiente.AVISO_SERVIR_AL_HABER_STOCK;

            string error = await gestor.CambiarServirAlHaberStock("1", 927519, false, "u");

            Assert.IsNull(error);
            Assert.AreEqual(SinFecha, lineas.Single().Fecha_Entrega);
            Assert.AreEqual("NOTA DE ENTREGA\r\n" + CreadorNotaEntregaPendiente.AVISO_SIN_FECHA, nota.Comentarios);
            A.CallTo(() => repositorio.Guardar("1", 927519, false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CambiarServirAlHaberStock_ConPicking_Rechaza_YNoToca()
        {
            var repositorio = RepositorioCon();
            Nota(927519, 927116, 730321, SinFecha, picking: 99700);

            string error = await gestor.CambiarServirAlHaberStock("1", 927519, true, "u");

            StringAssert.Contains(error, "picking");
            Assert.AreEqual(SinFecha, lineas.Single().Fecha_Entrega);
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarServirAlHaberStock_SinElScript_Rechaza_YNoToca()
        {
            RepositorioCon(columnas: false);
            Nota(927519, 927116, 730321, SinFecha);

            string error = await gestor.CambiarServirAlHaberStock("1", 927519, true, "u");

            Assert.AreEqual(GestorFechaEntregaNotas.MOTIVO_SIN_COLUMNA, error);
            Assert.AreEqual(SinFecha, lineas.Single().Fecha_Entrega);
        }

        [TestMethod]
        public async Task CambiarServirAlHaberStock_NoEsNota_Rechaza()
        {
            RepositorioCon();
            cabeceras.Add(new CabPedidoVta { Empresa = "1", Número = 927116, NotaEntrega = false });

            string error = await gestor.CambiarServirAlHaberStock("1", 927116, true, "u");

            StringAssert.Contains(error, "nota de entrega");
        }

        private static DbSet<T> DbSetCon<T>(List<T> datos) where T : class
        {
            var fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return fake;
        }
    }
}
