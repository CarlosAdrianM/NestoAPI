using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Tests del helper <see cref="TransicionPresupuesto"/> (NestoAPI#193).
    /// Verifican que el PUT distingue "pasar a presupuesto" de "aceptar presupuesto"
    /// usando el estado actual en BD y no solo la flag EsPresupuesto del DTO.
    /// </summary>
    [TestClass]
    public class TransicionPresupuestoTests
    {
        [TestMethod]
        public void Decidir_TodasPendientesYDTOTodasPresupuesto_EsPasarAPresupuesto()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0)
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.PRESUPUESTO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsTrue(decision.EsPasarAPresupuesto);
            Assert.IsFalse(decision.EsAceptarPresupuesto);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, decision.IdsParaPresupuesto.ToArray());
        }

        [TestMethod]
        public void Decidir_TodasEnCursoYDTOTodasPresupuesto_EsPasarAPresupuesto()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(10, Constantes.EstadosLineaVenta.EN_CURSO, picking: 0)
            };
            var dto = DtoConLineas(LineaDto(10, Constantes.EstadosLineaVenta.PRESUPUESTO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsTrue(decision.EsPasarAPresupuesto);
            Assert.IsTrue(decision.IdsParaPresupuesto.Contains(10));
        }

        [TestMethod]
        public void Decidir_LineaConPicking_NoSeIncluyeEnIds()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 555) // ya tiene picking
            };
            // El cliente solo marca como presupuesto la elegible (la que no tiene picking).
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.EN_CURSO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsTrue(decision.EsPasarAPresupuesto);
            CollectionAssert.AreEquivalent(new[] { 1 }, decision.IdsParaPresupuesto.ToArray());
        }

        [TestMethod]
        public void Decidir_TodasConPicking_NoEsPasarAPresupuesto()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 100),
                LineaBD(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 200)
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.PRESUPUESTO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsFalse(decision.EsPasarAPresupuesto);
            Assert.IsFalse(decision.EsAceptarPresupuesto);
            Assert.AreEqual(0, decision.IdsParaPresupuesto.Count);
        }

        [TestMethod]
        public void Decidir_LineasMixtasConAlbaranYPendientes_PasarAPresupuestoSoloEnElegibles()
        {
            // En este escenario el controller validará luego que no se mezclan
            // (No se pueden mezclar pedidos con presupuestos), pero el helper
            // debe quedarse con las elegibles independientemente.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.ALBARAN, picking: 0)
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.ALBARAN));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsTrue(decision.EsPasarAPresupuesto);
            CollectionAssert.AreEquivalent(new[] { 1 }, decision.IdsParaPresupuesto.ToArray());
        }

        [TestMethod]
        public void Decidir_TodasEnPresupuestoBDYDTOQuiereSalir_EsAceptarPresupuesto()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0)
            };
            // Cliente envía EsPresupuesto=false (es lo que hace OnAceptarPresupuesto).
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.PRESUPUESTO));
            dto.EsPresupuesto = false;

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsTrue(decision.EsAceptarPresupuesto);
            Assert.IsFalse(decision.EsPasarAPresupuesto);
        }

        [TestMethod]
        public void Decidir_TodasEnPresupuestoPeroEsPresupuestoTrue_NoEsAceptarPresupuesto()
        {
            // Si el DTO sigue diciendo EsPresupuesto=true, no estamos aceptando.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0)
            };
            var dto = DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO));
            dto.EsPresupuesto = true;

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsFalse(decision.EsAceptarPresupuesto);
            Assert.IsFalse(decision.EsPasarAPresupuesto);
        }

        [TestMethod]
        public void Decidir_PedidoSinCambiosDeEstado_NingunaTransicion()
        {
            // Caso típico: el usuario solo cambia un dato del pedido (texto, dirección...)
            // sin tocar estados. No debe activarse ninguna transición.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0)
            };
            var dto = DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.PENDIENTE));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsFalse(decision.EsPasarAPresupuesto);
            Assert.IsFalse(decision.EsAceptarPresupuesto);
        }

        [TestMethod]
        public void Decidir_AmbasTransicionesNuncaCoexisten()
        {
            // Sanity check: en cualquier combinación de inputs razonables, no podemos
            // decir a la vez que es "aceptar" y "pasar a" presupuesto.
            var escenarios = new (List<LinPedidoVta> bd, PedidoVentaDTO dto)[]
            {
                (new List<LinPedidoVta>
                    {
                        LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0)
                    },
                    DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO))),
                (new List<LinPedidoVta>
                    {
                        LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0)
                    },
                    DtoConLineasYEsPresupuesto(false,
                        LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO))),
                (new List<LinPedidoVta>
                    {
                        LineaBD(1, Constantes.EstadosLineaVenta.EN_CURSO, picking: 0)
                    },
                    DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.EN_CURSO)))
            };

            foreach (var (bd, dto) in escenarios)
            {
                var decision = TransicionPresupuesto.Decidir(bd, dto);
                Assert.IsFalse(
                    decision.EsAceptarPresupuesto && decision.EsPasarAPresupuesto,
                    "Las dos transiciones nunca deben darse simultáneamente.");
            }
        }

        // ----- helpers -----

        private static LinPedidoVta LineaBD(int numeroOrden, short estado, int picking)
        {
            return new LinPedidoVta
            {
                Nº_Orden = numeroOrden,
                Estado = estado,
                Picking = picking
            };
        }

        private static LineaPedidoVentaDTO LineaDto(int id, short estado)
        {
            return new LineaPedidoVentaDTO
            {
                id = id,
                estado = estado
            };
        }

        private static PedidoVentaDTO DtoConLineas(params LineaPedidoVentaDTO[] lineas)
        {
            var dto = new PedidoVentaDTO();
            foreach (var l in lineas)
            {
                dto.Lineas.Add(l);
            }
            return dto;
        }

        private static PedidoVentaDTO DtoConLineasYEsPresupuesto(bool esPresupuesto, params LineaPedidoVentaDTO[] lineas)
        {
            var dto = DtoConLineas(lineas);
            dto.EsPresupuesto = esPresupuesto;
            return dto;
        }
    }
}
