using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Reclamaciones;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (Carlos 27/11/26): «error de envío reclamado» (reenviado sin coste, cambiado por otro, abonado sin reposición).</summary>
    [TestClass]
    public class ReglaErrorEnvioReclamadoTests
    {
        private static readonly DateTime ALBARAN = new DateTime(2026, 9, 1);
        private static readonly DateTime HOY = new DateTime(2026, 11, 27);

        private static LineaServidaReclamacion Servida(int pedido = 900000, int linea = 1, string producto = "41262", decimal baseImponible = 20m,
            DateTime? albaran = null, string subGrupo = "COS", string familia = "Eva Visnu") => new LineaServidaReclamacion
            {
                Pedido = pedido,
                Linea = linea,
                Producto = producto,
                Cliente = "15000",
                FechaAlbaran = albaran ?? ALBARAN,
                Factura = "NV26/1000",
                Cantidad = 1,
                BaseImponible = baseImponible,
                Grupo = "COS",
                SubGrupo = subGrupo,
                Familia = familia,
                PrecioTarifa = 20m
            };

        private static LineaPosteriorReclamacion Posterior(int pedido = 900100, string producto = "41262", decimal baseImponible = 0m, DateTime? fecha = null,
            int cantidad = 1, string subGrupo = "COS", string familia = "Eva Visnu", bool enRectificativa = false, string rectificativa = null) =>
            new LineaPosteriorReclamacion
            {
                EnRectificativa = enRectificativa,
                FacturaRectificativa = rectificativa,
                Cliente = "15000",
                Pedido = pedido,
                Linea = 1,
                Fecha = fecha ?? ALBARAN.AddDays(5),
                Producto = producto,
                Cantidad = cantidad,
                BaseImponible = baseImponible,
                Grupo = "COS",
                SubGrupo = subGrupo,
                Familia = familia
            };

        private static ResultadoReclamacion Uno(LecturaReclamacionesEnvio lectura, DateTime? hoy = null, int dias = 45) =>
            ReglaErrorEnvioReclamado.Evaluar(lectura, hoy ?? HOY, dias).Values.Single(r => r.Pedido == 900000 && r.Linea == 1);

        [TestMethod]
        public void Dias_SinValorOMal_45YConValor_ElValor()
        {
            Assert.AreEqual(45, ReglaErrorEnvioReclamado.Dias(null));
            Assert.AreEqual(45, ReglaErrorEnvioReclamado.Dias("abc"));
            Assert.AreEqual(45, ReglaErrorEnvioReclamado.Dias("0"));
            Assert.AreEqual(45, ReglaErrorEnvioReclamado.Dias("400"));
            Assert.AreEqual(30, ReglaErrorEnvioReclamado.Dias(" 30 "));
        }

        [TestMethod]
        public void MismoProductoSinCosteDespues_ReenviadoSinCosteYErrorConSuPrueba()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                MismoProducto = { Posterior() }
            };

            ResultadoReclamacion r = Uno(lectura);

            Assert.IsTrue(r.Evaluada);
            Assert.IsTrue(r.ErrorReclamado);
            CollectionAssert.AreEquivalent(new[] { SubMotivoReclamacion.ReenviadoSinCoste }, r.SubMotivos.ToList());
            PruebaReclamacion prueba = r.Pruebas.Single();
            Assert.AreEqual(900100, prueba.Pedido);
            Assert.AreEqual("41262", prueba.Producto);
            Assert.AreEqual(0m, prueba.Importe);
        }

        [TestMethod]
        public void ReenvioQueEsRegalo_NoCuenta()
        {
            LineaPosteriorReclamacion muestra = Posterior(pedido: 900101, subGrupo: "MMP");
            LineaPosteriorReclamacion deOferta = Posterior(pedido: 900102);
            deOferta.Oferta = 77;
            LineaPosteriorReclamacion ganavisiones = Posterior(pedido: 900103);
            ganavisiones.EsGanavisiones = true;
            LineaPosteriorReclamacion porImporte = Posterior(pedido: 900104);
            porImporte.EsRegaloPorImporte = true;
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                MismoProducto = { muestra, deOferta, ganavisiones, porImporte }
            };

            ResultadoReclamacion r = Uno(lectura);

            Assert.IsFalse(r.ErrorReclamado);
            Assert.AreEqual(0, r.SubMotivos.Count);
        }

        [TestMethod]
        public void OriginalYaGratis_NoCuenta()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida(baseImponible: 0m) },
                MismoProducto = { Posterior() }
            };

            Assert.IsFalse(Uno(lectura).ErrorReclamado);
        }

        [TestMethod]
        public void PosteriorConCoste_FueraDeVentana_MismoPedidoONotaDeLoPendiente_NoCuentan()
        {
            LineaPosteriorReclamacion notaPendiente = Posterior(pedido: 900105);
            notaPendiente.PedidoOrigen = 900000;
            notaPendiente.NotaEntrega = true;
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                MismoProducto =
                {
                    Posterior(pedido: 900101, baseImponible: 20m),
                    Posterior(pedido: 900102, fecha: ALBARAN.AddDays(46)),
                    Posterior(pedido: 900103, fecha: ALBARAN.AddDays(-1)),
                    Posterior(pedido: 900000),
                    notaPendiente
                }
            };

            Assert.IsFalse(Uno(lectura).ErrorReclamado);
        }

        [TestMethod]
        public void ServidaHaceMenosDeLosDias_NoEvaluadaPeroSeVeLaPrueba()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida(albaran: HOY.AddDays(-10)) },
                MismoProducto = { Posterior(fecha: HOY.AddDays(-5)) }
            };

            ResultadoReclamacion r = Uno(lectura);

            Assert.IsFalse(r.Evaluada);
            Assert.IsTrue(r.ErrorReclamado);
            Assert.IsNull(ReglaErrorEnvioReclamado.Combinar(new[] { r }));
        }

        [TestMethod]
        public void DosCompras_ElReenvioSeImputaSoloALaUltimaAnterior()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida(), Servida(pedido: 900050, albaran: ALBARAN.AddDays(3)) },
                MismoProducto = { Posterior(fecha: ALBARAN.AddDays(5)) }
            };

            Dictionary<Tuple<int, int>, ResultadoReclamacion> r = ReglaErrorEnvioReclamado.Evaluar(lectura, HOY, 45);

            Assert.IsFalse(r[Tuple.Create(900000, 1)].ErrorReclamado);
            Assert.IsTrue(r[Tuple.Create(900050, 1)].ErrorReclamado);
        }

        [TestMethod]
        public void Rectificada_YEnLaRectificativaSeCargaOtroParecido_CambiadoPorOtro()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                Rectificaciones = { new RectificacionLineaReclamacion { Pedido = 900000, Linea = 1, FacturaRectificativa = "RV26/10", Fecha = ALBARAN.AddDays(10), Cantidad = 1 } },
                OtrosProductos =
                {
                    Posterior(pedido: 900200, producto: "41262", cantidad: -1, baseImponible: -20m, enRectificativa: true, rectificativa: "RV26/10"),
                    Posterior(pedido: 900200, producto: "41263", baseImponible: 21m, enRectificativa: true, rectificativa: "RV26/10")
                }
            };
            lectura.OtrosProductos[1].Linea = 2;

            ResultadoReclamacion r = Uno(lectura);

            Assert.IsTrue(r.ErrorReclamado);
            CollectionAssert.AreEquivalent(new[] { SubMotivoReclamacion.CambiadoPorOtro }, r.SubMotivos.ToList());
            Assert.AreEqual("41263", r.Pruebas.Single().Producto);
            Assert.AreEqual("RV26/10", r.Pruebas.Single().FacturaRectificativa);
        }

        [TestMethod]
        public void Rectificada_ConRefacturacionDeOtroProducto_NoEsCambio()
        {
            // La rectificativa abona y vuelve a cargar el 41263 (precio o cliente): no es un cambio de producto
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                Rectificaciones = { new RectificacionLineaReclamacion { Pedido = 900000, Linea = 1, FacturaRectificativa = "RV26/10", Fecha = ALBARAN.AddDays(10), Cantidad = 1 } },
                OtrosProductos =
                {
                    Posterior(pedido: 900200, producto: "41263", cantidad: -1, baseImponible: -21m, enRectificativa: true, rectificativa: "RV26/10"),
                    Posterior(pedido: 900200, producto: "41263", baseImponible: 19m, enRectificativa: true, rectificativa: "RV26/10")
                }
            };
            lectura.OtrosProductos[1].Linea = 2;

            ResultadoReclamacion r = Uno(lectura);

            CollectionAssert.AreEquivalent(new[] { SubMotivoReclamacion.AbonadoSinReposicion }, r.SubMotivos.ToList());
            Assert.IsFalse(r.ErrorReclamado);
        }

        [TestMethod]
        public void Rectificada_YSinCosteOtroParecidoCercaDeLaRectificativa_CambiadoPorOtro_PeroNoSiEsDeOtraFamiliaOLejos()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                Rectificaciones = { new RectificacionLineaReclamacion { Pedido = 900000, Linea = 1, FacturaRectificativa = "RV26/10", Fecha = ALBARAN.AddDays(10), Cantidad = 1 } },
                OtrosProductos = { Posterior(pedido: 900300, producto: "41263", fecha: ALBARAN.AddDays(12)) }
            };
            Assert.IsTrue(Uno(lectura).SubMotivos.Contains(SubMotivoReclamacion.CambiadoPorOtro));

            lectura.OtrosProductos[0] = Posterior(pedido: 900300, producto: "99999", fecha: ALBARAN.AddDays(12), subGrupo: "OTR", familia: "Otra");
            Assert.IsFalse(Uno(lectura).ErrorReclamado);

            lectura.OtrosProductos[0] = Posterior(pedido: 900300, producto: "41263", fecha: ALBARAN.AddDays(30));
            Assert.IsFalse(Uno(lectura).ErrorReclamado);

            // Cargado (con coste) en un pedido normal: no cuenta (demasiado laxo con quien compra cada semana)
            lectura.OtrosProductos[0] = Posterior(pedido: 900300, producto: "41263", fecha: ALBARAN.AddDays(12), baseImponible: 20m);
            Assert.IsFalse(Uno(lectura).ErrorReclamado);
        }

        [TestMethod]
        public void RectificadaSinNadaMas_AbonadoSinReposicion_Informativo()
        {
            var lectura = new LecturaReclamacionesEnvio
            {
                Servidas = { Servida() },
                Rectificaciones = { new RectificacionLineaReclamacion { Pedido = 900000, Linea = 1, FacturaRectificativa = "RV26/10", Fecha = ALBARAN.AddDays(10), Cantidad = 1 } }
            };

            ResultadoReclamacion r = Uno(lectura);

            CollectionAssert.AreEquivalent(new[] { SubMotivoReclamacion.AbonadoSinReposicion }, r.SubMotivos.ToList());
            Assert.IsFalse(r.ErrorReclamado);
            Assert.AreEqual(false, ReglaErrorEnvioReclamado.Combinar(new[] { r }));
            Assert.AreEqual("RV26/10", r.Pruebas.Single().FacturaRectificativa);
        }

        [TestMethod]
        public void Combinar_ErrorEvaluadoMandaYFaltaUnaLineaEsNoEvaluado()
        {
            var bien = new ResultadoReclamacion { Evaluada = true };
            var mal = new ResultadoReclamacion { Evaluada = true };
            _ = mal.SubMotivos.Add(SubMotivoReclamacion.ReenviadoSinCoste);
            var reciente = new ResultadoReclamacion { Evaluada = false };

            Assert.IsNull(ReglaErrorEnvioReclamado.Combinar(new ResultadoReclamacion[0]));
            Assert.AreEqual(false, ReglaErrorEnvioReclamado.Combinar(new[] { bien }));
            Assert.IsNull(ReglaErrorEnvioReclamado.Combinar(new[] { bien }, 2));
            Assert.IsNull(ReglaErrorEnvioReclamado.Combinar(new[] { bien, reciente }));
            Assert.AreEqual(true, ReglaErrorEnvioReclamado.Combinar(new[] { mal, reciente }));
        }
    }
}
