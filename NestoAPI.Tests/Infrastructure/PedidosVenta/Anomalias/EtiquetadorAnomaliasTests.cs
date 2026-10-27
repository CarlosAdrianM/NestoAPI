using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta.Anomalias;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: el etiquetado automático (CorregidoEn48h / Abonado / SinCambios) y el job nocturno.</summary>
    [TestClass]
    public class EtiquetadorAnomaliasTests
    {
        private static readonly DateTime GRABADO = new DateTime(2026, 10, 21, 12, 0, 0);

        private static FilaAnomaliaPedido FilaLinea(int? linea = 5, string producto = "38406")
        {
            return new FilaAnomaliaPedido
            {
                Id = 1,
                Fecha = GRABADO,
                Empresa = "1",
                Pedido = 929057,
                Cliente = "10257",
                Linea = linea,
                Producto = producto,
                Regla = ReglasAnomalia.CANTIDAD_ALTA,
                CantidadGrabada = 30,
                PrecioGrabado = 20.45m,
                DescuentoGrabado = 0.22m
            };
        }

        private static FilaAnomaliaPedido FilaPedido()
        {
            return new FilaAnomaliaPedido
            {
                Id = 2,
                Fecha = GRABADO,
                Empresa = "1",
                Pedido = 929057,
                Cliente = "10257",
                Regla = ReglasAnomalia.CONTEXTO,
                ImportePedido = 613.50m,
                NumeroLineas = 1
            };
        }

        private static EstadoLineaActual Actual(int cantidad = 30, decimal precio = 20.45m, decimal descuento = 0.22m, DateTime? modificada = null, int linea = 5, string producto = "38406")
        {
            return new EstadoLineaActual { Empresa = "1", Pedido = 929057, Linea = linea, Producto = producto, Cantidad = cantidad, Precio = precio, Descuento = descuento, FechaModificacion = modificada ?? GRABADO };
        }

        /// <summary>El JSON que guarda PedidosVentaController en Modificaciones.Nuevo, reducido a lo que importa.</summary>
        private static ModificacionCruda Json(DateTime fecha, params string[] lineas)
        {
            return new ModificacionCruda
            {
                Id = 1,
                Fecha = fecha,
                Usuario = "NUEVAVISION\\Carlos",
                Nuevo = "{\"Agrupada\":false,\"cliente\":\"10257\",\"empresa\":\"1\",\"numero\":929057,\"Lineas\":[" + string.Join(",", lineas) + "],\"Usuario\":\"NUEVAVISION\\\\Carlos\"}"
            };
        }

        private static string LineaJson(int id, string producto, int cantidad, decimal precio, decimal descuentoLinea = 0, decimal descuentoProducto = 0.22m, byte tipo = 1)
            => "{\"id\":" + id + ",\"Producto\":\"" + producto + "\",\"Cantidad\":" + cantidad + ",\"PrecioUnitario\":" + precio.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ",\"DescuentoLinea\":" + descuentoLinea.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"DescuentoProducto\":" + descuentoProducto.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ",\"DescuentoEntidad\":0.0,\"AplicarDescuento\":true,\"tipoLinea\":" + tipo + ",\"EsChequeRegalo\":false}";

        private static List<ModificacionPedido> Parsear(params ModificacionCruda[] crudas) => crudas.Select(ModificacionPedido.Parsear).Where(m => m != null).ToList();

        [TestMethod]
        public void ModificacionPedido_Parsear_LeeElPedidoYLasLineasDeProducto()
        {
            ModificacionPedido modificacion = ModificacionPedido.Parsear(Json(GRABADO.AddHours(1),
                LineaJson(5, "38406", 3, 20.45m), LineaJson(6, "TEXTO", 1, 0, 0, 0, 2), LineaJson(0, "25720", 2, 15.95m, 0.10m, 0)));

            Assert.IsTrue(modificacion.Parseada);
            Assert.AreEqual(929057, modificacion.Pedido);
            Assert.AreEqual(2, modificacion.Lineas.Count, "la de texto no cuenta");
            Assert.AreEqual(0.22m, modificacion.Lineas[0].Descuento);
            Assert.AreEqual(0.10m, modificacion.Lineas[1].Descuento);
            Assert.AreEqual(3 * 20.45m + 2 * 15.95m, modificacion.ImporteBruto);
        }

        [TestMethod]
        public void ModificacionPedido_Parsear_JsonRotoONoDePedido()
        {
            Assert.IsNull(ModificacionPedido.Parsear(new ModificacionCruda { Nuevo = "{\"otra\":1}" }));
            Assert.IsNull(ModificacionPedido.Parsear(new ModificacionCruda { Nuevo = "no es json" }));
            Assert.IsNull(ModificacionPedido.Parsear(null));
        }

        [TestMethod]
        public void Etiquetar_LineaCambiadaEnUnPutDentroDe48h_CorregidoEn48h()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime corregido = GRABADO.AddHours(3);
            List<ModificacionPedido> modificaciones = Parsear(Json(corregido, LineaJson(5, "38406", 3, 20.45m)));

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, modificada: GRABADO.AddDays(5)) }, modificaciones, null, GRABADO.AddDays(6)));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(corregido, fila.ResueltoEl, "la fecha del PUT, aunque LinPedidoVta se tocara después (albarán)");
        }

        [TestMethod]
        public void Etiquetar_ElPropioPutQueGraboLaFila_NoCuentaComoCorreccion()
        {
            // La Modificación del mismo guardado trae la línea tal y como se grabó (y un segundo antes por el reloj de la BD).
            FilaAnomaliaPedido fila = FilaLinea();
            List<ModificacionPedido> modificaciones = Parsear(Json(GRABADO.AddSeconds(-1), LineaJson(5, "38406", 30, 20.45m)));

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, modificaciones, null, GRABADO.AddDays(1)));
            Assert.IsNull(fila.Resultado);
        }

        [TestMethod]
        public void Etiquetar_LineaBorradaEnUnPutDentroDe48h_CorregidoEn48h()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime borrado = GRABADO.AddHours(20);
            List<ModificacionPedido> modificaciones = Parsear(Json(borrado, LineaJson(6, "25720", 1, 15.95m)));

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new List<EstadoLineaActual>(), modificaciones, null, GRABADO.AddDays(10)));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(borrado, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_LineaBorradaSinRastroDentroDe48h_CorregidoEn48h()
        {
            // Nesto viejo o DELETE del pedido: no hay JSON, pero aún estamos en plazo cuando el job la mira.
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime ahora = GRABADO.AddHours(18);

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new List<EstadoLineaActual>(), new List<ModificacionPedido>(), null, ahora));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(ahora, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_LineaBorradaSinRastroPasadas48h_SinCambiosALos7Dias()
        {
            FilaAnomaliaPedido fila = FilaLinea();

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new List<EstadoLineaActual>(), new List<ModificacionPedido>(), null, GRABADO.AddDays(3)));
            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new List<EstadoLineaActual>(), new List<ModificacionPedido>(), null, GRABADO.AddDays(7)));
            Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, fila.Resultado);
        }

        [TestMethod]
        public void Etiquetar_LineaCambiadaEnLinPedidoVtaDentroDe48h_CorregidoEn48h()
        {
            // Cambiada desde Nesto viejo (sin pasar por la API): lo dice [Fecha Modificación] y los valores.
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime cambio = GRABADO.AddHours(30);

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, modificada: cambio) }, new List<ModificacionPedido>(), null, GRABADO.AddDays(2)));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(cambio, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_LineaCambiadaPasadas48h_NoEsCorreccion()
        {
            FilaAnomaliaPedido fila = FilaLinea();

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, modificada: GRABADO.AddDays(3)) }, new List<ModificacionPedido>(), null, GRABADO.AddDays(4)));
            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, modificada: GRABADO.AddDays(3)) }, new List<ModificacionPedido>(), null, GRABADO.AddDays(7)));
            Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, fila.Resultado);
        }

        [TestMethod]
        public void Etiquetar_SoloCambiaElEstado_NoEsCorreccion()
        {
            // El albarán toca [Fecha Modificación] pero no cantidad, precio ni descuento.
            FilaAnomaliaPedido fila = FilaLinea();

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(modificada: GRABADO.AddHours(5)) }, new List<ModificacionPedido>(), null, GRABADO.AddDays(1)));
        }

        [TestMethod]
        public void Etiquetar_AntesDe7DiasSinCambios_SigueEsperando()
        {
            FilaAnomaliaPedido fila = FilaLinea();

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, new List<ModificacionPedido>(), null, GRABADO.AddDays(6).AddHours(23)));
            Assert.IsNull(fila.Resultado);
            Assert.IsNull(fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_ALos7DiasSinCambios_SinCambios()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime ahora = GRABADO.AddDays(7);

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, new List<ModificacionPedido>(), new List<AbonoCliente>(), ahora));
            Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, fila.Resultado);
            Assert.AreEqual(ahora, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_ALos7DiasConDevolucionDelProducto_Abonado()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            DateTime abono = GRABADO.AddDays(4);
            var abonos = new List<AbonoCliente>
            {
                new AbonoCliente { Empresa = "1", Cliente = "10257", Producto = "25720", Pedido = 930000, Fecha = abono }, // otro producto
                new AbonoCliente { Empresa = "1", Cliente = "10257", Producto = "38406", Pedido = 930001, Fecha = abono }
            };

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, new List<ModificacionPedido>(), abonos, GRABADO.AddDays(7)));
            Assert.AreEqual(ResultadosAnomalia.ABONADO, fila.Resultado);
            Assert.AreEqual(abono, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_DevolucionAnteriorAGrabar_NoEsAbono()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            var abonos = new List<AbonoCliente> { new AbonoCliente { Cliente = "10257", Producto = "38406", Pedido = 920000, Fecha = GRABADO.AddDays(-1) } };

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, new List<ModificacionPedido>(), abonos, GRABADO.AddDays(7)));
            Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, fila.Resultado);
        }

        [TestMethod]
        public void Etiquetar_LineaSinNumeroDeOrden_SeSiguePorProducto()
        {
            FilaAnomaliaPedido fila = FilaLinea(linea: null);
            DateTime corregido = GRABADO.AddHours(1);
            List<ModificacionPedido> modificaciones = Parsear(Json(corregido, LineaJson(9, "38406", 3, 20.45m)));

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, linea: 9) }, modificaciones, null, GRABADO.AddDays(1)));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(corregido, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_FilaDelPedido_CambiaElNumeroDeLineasOElBruto()
        {
            FilaAnomaliaPedido fila = FilaPedido();
            DateTime corregido = GRABADO.AddHours(2);
            List<ModificacionPedido> modificaciones = Parsear(Json(corregido, LineaJson(5, "38406", 3, 20.45m)));

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3) }, modificaciones, null, GRABADO.AddDays(1)));
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, fila.Resultado);
            Assert.AreEqual(corregido, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_FilaDelPedido_MismoBrutoYLineas_NoEsCorreccion()
        {
            FilaAnomaliaPedido fila = FilaPedido();
            List<ModificacionPedido> modificaciones = Parsear(Json(GRABADO.AddHours(2), LineaJson(5, "38406", 30, 20.45m)));

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, modificaciones, null, GRABADO.AddDays(1)));
        }

        [TestMethod]
        public void Etiquetar_FilaDelPedido_BrutoActualDistintoDentroDe48h_CorregidoEn48h()
        {
            FilaAnomaliaPedido fila = FilaPedido();
            DateTime cambio = GRABADO.AddHours(40);

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual(3, modificada: cambio) }, new List<ModificacionPedido>(), null, GRABADO.AddDays(2)));
            Assert.AreEqual(cambio, fila.ResueltoEl);
        }

        [TestMethod]
        public void Etiquetar_FilaDelPedido_AbonoDeUnProductoDelPedido()
        {
            FilaAnomaliaPedido fila = FilaPedido();
            var abonos = new List<AbonoCliente> { new AbonoCliente { Cliente = "10257", Producto = "38406", Pedido = 930001, Fecha = GRABADO.AddDays(5) } };

            Assert.IsTrue(EtiquetadorAnomalias.Etiquetar(fila, new[] { Actual() }, new List<ModificacionPedido>(), abonos, GRABADO.AddDays(7)));
            Assert.AreEqual(ResultadosAnomalia.ABONADO, fila.Resultado);
        }

        [TestMethod]
        public void Etiquetar_FilaYaEtiquetada_NoSeToca()
        {
            FilaAnomaliaPedido fila = FilaLinea();
            fila.Resultado = ResultadosAnomalia.ABONADO;

            Assert.IsFalse(EtiquetadorAnomalias.Etiquetar(fila, new List<EstadoLineaActual>(), new List<ModificacionPedido>(), null, GRABADO.AddDays(30)));
            Assert.AreEqual(ResultadosAnomalia.ABONADO, fila.Resultado);
        }

        [TestMethod]
        public void EtiquetarPendientes_RecorreLasPendientesYGuardaLasEtiquetadas()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria();
            repositorio.Insertar(new List<FilaAnomaliaPedido>
            {
                FilaLinea(),                                                        // corregida por el JSON
                new FilaAnomaliaPedido { Fecha = GRABADO, Empresa = "1", Pedido = 929058, Cliente = "20000", Regla = ReglasAnomalia.CONTEXTO, ImportePedido = 10m, NumeroLineas = 1 }, // 7 días: sin cambios
                new FilaAnomaliaPedido { Fecha = GRABADO.AddDays(6), Empresa = "1", Pedido = 929059, Cliente = "30000", Regla = ReglasAnomalia.CONTEXTO, ImportePedido = 10m, NumeroLineas = 1 }  // espera
            });
            repositorio.Modificaciones.Add(Json(GRABADO.AddHours(2), LineaJson(5, "38406", 3, 20.45m)));
            repositorio.LineasActuales.Add(Actual(3));
            repositorio.LineasActuales.Add(new EstadoLineaActual { Empresa = "1", Pedido = 929058, Linea = 1, Producto = "X", Cantidad = 1, Precio = 10m, FechaModificacion = GRABADO });
            repositorio.LineasActuales.Add(new EstadoLineaActual { Empresa = "1", Pedido = 929059, Linea = 1, Producto = "Y", Cantidad = 1, Precio = 10m, FechaModificacion = GRABADO.AddDays(6) });

            List<FilaAnomaliaPedido> etiquetadas = EtiquetadorAnomalias.EtiquetarPendientes(repositorio, GRABADO.AddDays(7));

            Assert.AreEqual(2, etiquetadas.Count);
            Assert.AreEqual(ResultadosAnomalia.CORREGIDO_48H, repositorio.Filas[0].Resultado);
            Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, repositorio.Filas[1].Resultado);
            Assert.IsNull(repositorio.Filas[2].Resultado);
        }

        [TestMethod]
        public void JobEtiquetado_Apagado_NoHaceNada()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria();
            repositorio.Insertar(new List<FilaAnomaliaPedido> { FilaLinea() });
            int creados = 0;
            try
            {
                InterruptorAnomalias.LeerAjuste = clave => "Apagado";
                AnomaliasJobsService.CrearRepositorio = () =>
                {
                    creados++;
                    return repositorio;
                };
                AnomaliasJobsService.Ahora = () => GRABADO.AddDays(10);

                Assert.AreEqual(0, AnomaliasJobsService.EtiquetarNocturno());
                Assert.AreEqual(0, creados);

                InterruptorAnomalias.LeerAjuste = clave => "Sombra";
                Assert.AreEqual(1, AnomaliasJobsService.EtiquetarNocturno());
                Assert.AreEqual(ResultadosAnomalia.SIN_CAMBIOS, repositorio.Filas[0].Resultado, "borrada sin rastro y pasados los 7 días");
            }
            finally
            {
                InterruptorAnomalias.LeerAjuste = null;
                AnomaliasJobsService.CrearRepositorio = () => RepositorioAnomaliasPedidoSql.DesdeConfiguracion();
                AnomaliasJobsService.Ahora = () => DateTime.Now;
            }
        }

        [TestMethod]
        public void JobEtiquetado_Fallo_VaAElmahYDevuelveCero()
        {
            var avisos = new List<Exception>();
            try
            {
                InterruptorAnomalias.LeerAjuste = clave => "Sombra";
                AnomaliasJobsService.CrearRepositorio = () => throw new InvalidOperationException("sin BD");
                AnomaliasJobsService.Registrador = avisos.Add;

                Assert.AreEqual(0, AnomaliasJobsService.EtiquetarNocturno());
                Assert.AreEqual(1, avisos.Count);
            }
            finally
            {
                InterruptorAnomalias.LeerAjuste = null;
                AnomaliasJobsService.CrearRepositorio = () => RepositorioAnomaliasPedidoSql.DesdeConfiguracion();
                AnomaliasJobsService.Registrador = ex => { };
            }
        }
    }
}
