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
        public void Decidir_TodasEnAlbaran_NoEsAceptarPresupuesto()
        {
            // Pedido 918386: todas las líneas en albarán (ninguna ACTIVA: ni pendiente, ni en
            // curso, ni presupuesto) y EsPresupuesto=false (cambio solo de cabecera, p.ej. el
            // CCC de un RCB). NO debe interpretarse como "aceptar presupuesto": no hay ningún
            // presupuesto que aceptar. Antes el .All() sobre la lista vacía de líneas activas
            // daba true por VERDAD VACUA -> EsAceptarPresupuesto=true -> el PUT disparaba la
            // validación del pedido y fallaba por descuentos colados al solo cambiar el CCC.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.ALBARAN, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.ALBARAN, picking: 0)
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.ALBARAN),
                LineaDto(2, Constantes.EstadosLineaVenta.ALBARAN));
            dto.EsPresupuesto = false;

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.IsFalse(decision.EsAceptarPresupuesto,
                "Un pedido todo en albarán no tiene ningún presupuesto que aceptar");
            Assert.IsFalse(decision.EsPasarAPresupuesto);
        }

        [TestMethod]
        public void Decidir_AlbaranYFacturaSinLineasActivas_NoEsAceptarPresupuesto()
        {
            // Otra combinación sin líneas activas (albarán + factura): tampoco hay presupuesto
            // que aceptar.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.ALBARAN, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.FACTURA, picking: 0)
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.ALBARAN),
                LineaDto(2, Constantes.EstadosLineaVenta.FACTURA));
            dto.EsPresupuesto = false;

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

        // ----- NestoAPI#503: presupuestos «a medias» (líneas en -3 y en -1 en el mismo pedido) -----

        [TestMethod]
        public void AplicarPasoAPresupuesto_LineaElegibleQueNoVieneEnElDTO_TambienPasaAPresupuesto()
        {
            // Regresión #503: el PUT aplicaba el cambio recorriendo el DTO; la línea 2, elegible pero
            // ausente del DTO, se quedaba en -1 mientras la 1 pasaba a -3.
            var linea1 = LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0);
            var linea2 = LineaBD(2, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0);
            var lineasBD = new List<LinPedidoVta> { linea1, linea2 };
            var dto = DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);
            int cambiadas = TransicionPresupuesto.AplicarPasoAPresupuesto(lineasBD, decision);

            Assert.IsTrue(decision.EsPasarAPresupuesto);
            Assert.AreEqual(2, cambiadas);
            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, linea1.Estado);
            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, linea2.Estado, "La línea que no venía en el DTO también debe pasar a presupuesto");
        }

        [TestMethod]
        public void AplicarPasoAPresupuesto_LineaConPicking_SeQuedaComoEstaba()
        {
            var conPicking = LineaBD(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 555);
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0),
                conPicking
            };
            var dto = DtoConLineas(
                LineaDto(1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                LineaDto(2, Constantes.EstadosLineaVenta.EN_CURSO));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);
            int cambiadas = TransicionPresupuesto.AplicarPasoAPresupuesto(lineasBD, decision);

            Assert.AreEqual(1, cambiadas);
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, conPicking.Estado);
        }

        [TestMethod]
        public void AplicarPasoAPresupuesto_SinTransicion_NoTocaNada()
        {
            var linea = LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0);
            var lineasBD = new List<LinPedidoVta> { linea };
            var dto = DtoConLineas(LineaDto(1, Constantes.EstadosLineaVenta.PENDIENTE));

            var decision = TransicionPresupuesto.Decidir(lineasBD, dto);

            Assert.AreEqual(0, TransicionPresupuesto.AplicarPasoAPresupuesto(lineasBD, decision));
            Assert.AreEqual(0, TransicionPresupuesto.AplicarPasoAPresupuesto(lineasBD, null));
            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, linea.Estado);
        }

        [TestMethod]
        public void EsPresupuestoVivo_TodasEditablesEnPresupuesto_True()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(3, Constantes.EstadosLineaVenta.FACTURA, picking: 0) // una factura vieja no cuenta
            };

            Assert.IsTrue(TransicionPresupuesto.EsPresupuestoVivo(lineasBD));
        }

        [TestMethod]
        public void EsPresupuestoVivo_PedidoNormalOSinLineas_False()
        {
            Assert.IsFalse(TransicionPresupuesto.EsPresupuestoVivo(new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0)
            }));
            Assert.IsFalse(TransicionPresupuesto.EsPresupuestoVivo(new List<LinPedidoVta>()));
            Assert.IsFalse(TransicionPresupuesto.EsPresupuestoVivo(null));
        }

        [TestMethod]
        public void EstadoLineaNueva_AmpliarUnPresupuestoVivo_NaceEnPresupuestoAunqueElDTODigaPendiente()
        {
            // Regresión #503 (el hueco de la plantilla): añadir líneas a un presupuesto sin aceptarlo
            // las creaba con el estado del DTO (-1) junto a las de -3.
            var sinTransicion = new TransicionPresupuesto.Decision();

            short estado = TransicionPresupuesto.EstadoLineaNueva(sinTransicion, pedidoEsPresupuestoVivoEnBD: true, estadoDto: Constantes.EstadosLineaVenta.PENDIENTE);

            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO, estado);
        }

        [TestMethod]
        public void EstadoLineaNueva_PasandoAPresupuesto_NaceEnPresupuesto()
        {
            var decision = new TransicionPresupuesto.Decision { EsPasarAPresupuesto = true };

            Assert.AreEqual(Constantes.EstadosLineaVenta.PRESUPUESTO,
                TransicionPresupuesto.EstadoLineaNueva(decision, pedidoEsPresupuestoVivoEnBD: false, estadoDto: Constantes.EstadosLineaVenta.EN_CURSO));
        }

        [TestMethod]
        public void EstadoLineaNueva_AceptandoPresupuesto_NuncaNaceEnPresupuesto()
        {
            var decision = new TransicionPresupuesto.Decision { EsAceptarPresupuesto = true };

            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO,
                TransicionPresupuesto.EstadoLineaNueva(decision, pedidoEsPresupuestoVivoEnBD: true, estadoDto: Constantes.EstadosLineaVenta.PRESUPUESTO));
            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE,
                TransicionPresupuesto.EstadoLineaNueva(decision, pedidoEsPresupuestoVivoEnBD: true, estadoDto: Constantes.EstadosLineaVenta.PENDIENTE));
        }

        [TestMethod]
        public void EstadoLineaNueva_PedidoNormal_RespetaElDTO()
        {
            var sinTransicion = new TransicionPresupuesto.Decision();

            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE,
                TransicionPresupuesto.EstadoLineaNueva(sinTransicion, pedidoEsPresupuestoVivoEnBD: false, estadoDto: Constantes.EstadosLineaVenta.PENDIENTE));
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO,
                TransicionPresupuesto.EstadoLineaNueva(null, pedidoEsPresupuestoVivoEnBD: false, estadoDto: Constantes.EstadosLineaVenta.EN_CURSO));
        }

        [TestMethod]
        public void HayMezclaPresupuesto_PresupuestoYPendienteSinPicking_True()
        {
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0)
            };

            Assert.IsTrue(TransicionPresupuesto.HayMezclaPresupuesto(lineasBD));
        }

        [TestMethod]
        public void HayMezclaPresupuesto_LasProtegidasNoCuentan()
        {
            // #193: al pasar a presupuesto, las líneas con picking, albarán o factura se quedan en su
            // estado. Eso no es una mezcla, es el diseño.
            var lineasBD = new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 555),
                LineaBD(3, Constantes.EstadosLineaVenta.ALBARAN, picking: 0),
                LineaBD(4, Constantes.EstadosLineaVenta.FACTURA, picking: 0)
            };

            Assert.IsFalse(TransicionPresupuesto.HayMezclaPresupuesto(lineasBD));
        }

        [TestMethod]
        public void HayMezclaPresupuesto_PedidoNormalOPresupuestoPuro_False()
        {
            Assert.IsFalse(TransicionPresupuesto.HayMezclaPresupuesto(new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PENDIENTE, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 0)
            }));
            Assert.IsFalse(TransicionPresupuesto.HayMezclaPresupuesto(new List<LinPedidoVta>
            {
                LineaBD(1, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0),
                LineaBD(2, Constantes.EstadosLineaVenta.PRESUPUESTO, picking: 0)
            }));
            Assert.IsFalse(TransicionPresupuesto.HayMezclaPresupuesto(null));
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
