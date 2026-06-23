using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): tests del núcleo de agrupación
    /// <see cref="MotorAgrupacionPedidos"/>.
    /// </summary>
    [TestClass]
    public class MotorAgrupacionPedidosTests
    {
        private NVEntities db;
        private MotorAgrupacionPedidos motor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            ConfigurarDbSet(s => A.CallTo(() => db.PedidosEspeciales).Returns(s), new List<PedidosEspeciale>());
            ConfigurarDbSet(s => A.CallTo(() => db.Ubicaciones).Returns(s), new List<Ubicacion>());
            motor = new MotorAgrupacionPedidos(db);
        }

        [TestMethod]
        public void Agrupar_DosPedidosUnDestino_MueveLineasYMarcaAgrupada()
        {
            CabPedidoVta destino = NuevoPedido("1", 100, "CLI-1", "0",
                NuevaLinea(orden: 1, contacto: "0", estado: Constantes.EstadosLineaVenta.ALBARAN));
            CabPedidoVta origen = NuevoPedido("1", 200, "CLI-1", "5",
                NuevaLinea(orden: 2, contacto: "5", estado: Constantes.EstadosLineaVenta.ALBARAN));

            CabPedidoVta resultado = motor.Agrupar(new[] { destino, origen }, destino);

            Assert.AreSame(destino, resultado);
            Assert.IsTrue(destino.Agrupada, "El destino debe quedar marcado como Agrupada.");
            // Las dos líneas acaban apuntando al pedido destino (Número 100).
            Assert.AreEqual(2, destino.LinPedidoVtas.Count);
            Assert.IsTrue(destino.LinPedidoVtas.All(l => l.Número == 100));
            // La línea del origen ya no cuelga del origen.
            Assert.AreEqual(0, origen.LinPedidoVtas.Count);
        }

        [TestMethod]
        public void Agrupar_PreservaContactoEnLinea_AunqueCabeceraDifiere()
        {
            CabPedidoVta destino = NuevoPedido("1", 100, "CLI-1", "0",
                NuevaLinea(orden: 1, contacto: "0", estado: Constantes.EstadosLineaVenta.ALBARAN));
            CabPedidoVta origen = NuevoPedido("1", 200, "CLI-1", "5",
                NuevaLinea(orden: 2, contacto: "5", estado: Constantes.EstadosLineaVenta.ALBARAN));

            motor.Agrupar(new[] { destino, origen }, destino);

            LinPedidoVta lineaMovida = destino.LinPedidoVtas.Single(l => l.Nº_Orden == 2);
            // El contacto/dirección de entrega de la línea NO cambia al moverla.
            Assert.AreEqual("5", lineaMovida.Contacto);
        }

        [TestMethod]
        public void Agrupar_ActualizaPedidosEspecialesQueApuntabanAOrigen()
        {
            CabPedidoVta destino = NuevoPedido("1", 100, "CLI-1", "0",
                NuevaLinea(orden: 1, contacto: "0", estado: Constantes.EstadosLineaVenta.ALBARAN));
            CabPedidoVta origen = NuevoPedido("1", 200, "CLI-1", "5",
                NuevaLinea(orden: 2, contacto: "5", estado: Constantes.EstadosLineaVenta.ALBARAN));

            var pedidoEspecial = new PedidosEspeciale { NºOrdenVta = 2, NºPedidoVta = 200 };
            ConfigurarDbSet(s => A.CallTo(() => db.PedidosEspeciales).Returns(s),
                new List<PedidosEspeciale> { pedidoEspecial });
            var ubicacion = new Ubicacion { NºOrdenVta = 2, PedidoVta = 200 };
            ConfigurarDbSet(s => A.CallTo(() => db.Ubicaciones).Returns(s),
                new List<Ubicacion> { ubicacion });

            motor.Agrupar(new[] { destino, origen }, destino);

            Assert.AreEqual(100, pedidoEspecial.NºPedidoVta,
                "El PedidoEspecial debe repuntar al pedido destino.");
            Assert.AreEqual(100, ubicacion.PedidoVta,
                "La ubicación debe repuntar al pedido destino.");
        }

        // ----- helpers -----

        private static CabPedidoVta NuevoPedido(string empresa, int numero, string cliente,
            string contacto, params LinPedidoVta[] lineas)
        {
            var pedido = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = cliente,
                Contacto = contacto,
                MantenerJunto = true,
                LinPedidoVtas = lineas.ToList()
            };
            foreach (LinPedidoVta l in pedido.LinPedidoVtas)
            {
                l.Empresa = empresa;
                l.Número = numero;
                l.CabPedidoVta = pedido;
            }
            return pedido;
        }

        private static LinPedidoVta NuevaLinea(int orden, string contacto, short estado)
        {
            return new LinPedidoVta
            {
                Nº_Orden = orden,
                Contacto = contacto,
                Estado = estado
            };
        }

        private static void ConfigurarDbSet<T>(System.Action<DbSet<T>> asignar, List<T> data) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            IQueryable<T> query = data.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(query.Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(query.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(query.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).Returns(query.GetEnumerator());
            asignar(set);
        }
    }
}
