using System.Collections.Generic;
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
        private IPersistenciaMovimientoLineas persistencia;
        private MotorAgrupacionPedidos motor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            // NestoAPI#729: el movimiento en la BD (SQL) y el soltar del contexto los hace la persistencia; aquí se simula.
            persistencia = A.Fake<IPersistenciaMovimientoLineas>();
            motor = new MotorAgrupacionPedidos(db, persistencia);
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

        /// <summary>
        /// NestoAPI#729 (regresión del 29/10/26): el motor NO cambia la clave de una entidad seguida por EF; manda a la BD las
        /// órdenes de las líneas que se mueven (las en albarán de los hermanos) y el grupo entero para soltarlo del contexto,
        /// ANTES de tocar el grafo en memoria.
        /// </summary>
        [TestMethod]
        public void Agrupar_MandaALaPersistenciaLasOrdenesMovidasYElGrupo_AntesDeTocarLaMemoria()
        {
            CabPedidoVta destino = NuevoPedido("1", 100, "CLI-1", "0",
                NuevaLinea(orden: 1, contacto: "0", estado: Constantes.EstadosLineaVenta.ALBARAN));
            CabPedidoVta origen = NuevoPedido("1", 200, "CLI-1", "5",
                NuevaLinea(orden: 2, contacto: "5", estado: Constantes.EstadosLineaVenta.ALBARAN),
                NuevaLinea(orden: 3, contacto: "5", estado: Constantes.EstadosLineaVenta.FACTURA));
            int numeroDeLaLinea2AlPersistir = -1;
            A.CallTo(() => persistencia.Mover("1", 100, A<IReadOnlyCollection<int>>._, A<IEnumerable<CabPedidoVta>>._))
                .Invokes(() => numeroDeLaLinea2AlPersistir = origen.LinPedidoVtas.Single(l => l.Nº_Orden == 2).Número);

            motor.Agrupar(new[] { destino, origen }, destino);

            A.CallTo(() => persistencia.Mover("1", 100,
                    A<IReadOnlyCollection<int>>.That.Matches(o => o.Count == 1 && o.Contains(2)),
                    A<IEnumerable<CabPedidoVta>>.That.Matches(g => g.Contains(destino) && g.Contains(origen))))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(200, numeroDeLaLinea2AlPersistir, "al persistir, la línea aún tiene su Número de origen (nada tocado en memoria)");
            Assert.AreEqual(200, origen.LinPedidoVtas.Single(l => l.Nº_Orden == 3).Número, "la facturada se queda en el origen");
        }

        [TestMethod]
        public void Agrupar_SinLineasQueMover_TambienPersisteLaMarcaDeAgrupado()
        {
            CabPedidoVta unico = NuevoPedido("1", 100, "CLI-1", "0",
                NuevaLinea(orden: 1, contacto: "0", estado: Constantes.EstadosLineaVenta.ALBARAN));

            motor.Agrupar(new[] { unico }, unico);

            A.CallTo(() => persistencia.Mover("1", 100,
                    A<IReadOnlyCollection<int>>.That.Matches(o => o.Count == 0),
                    A<IEnumerable<CabPedidoVta>>.That.Matches(g => g.Contains(unico))))
                .MustHaveHappenedOnceExactly();
            Assert.IsTrue(unico.Agrupada);
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

    }
}
