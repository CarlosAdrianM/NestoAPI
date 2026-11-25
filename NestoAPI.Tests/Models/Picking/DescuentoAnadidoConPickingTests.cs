using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#900 (pedido 930535, clienta 40513, 24/11/26): el cupón EV-2611-RG88 añadido a un pedido con el picking 100155 vivo se
    /// quedaba sin picking y Alfredo tuvo que albaranarlo a mano. La línea toma ese picking si llega a su mínimo; lo demás, como antes.
    /// </summary>
    [TestClass]
    public class DescuentoAnadidoConPickingTests
    {
        private const string CUPON = "CUPONEVENTO";
        private const string CHEQUE = "CHEQUE50_OCT26";
        private const int PICKING = 100155;
        private static readonly DateTime HOY = new DateTime(2026, 11, 24);

        private static LinPedidoVta Linea(int orden, string producto, short cantidad, decimal baseImponible, decimal total, short estado, int picking,
            DateTime? fecha = null)
            => new LinPedidoVta
            {
                Empresa = "1",
                Número = 930535,
                Nº_Orden = orden,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Almacén = Constantes.Productos.ALMACEN_POR_DEFECTO,
                Cantidad = cantidad,
                Base_Imponible = baseImponible,
                Total = total,
                Estado = estado,
                Picking = picking,
                Fecha_Entrega = fecha ?? new DateTime(2026, 11, 23)
            };

        private static List<LinPedidoVta> PedidoConPickingVivo() => new List<LinPedidoVta>
        {
            Linea(1, "CREMA", 2, 123.97M, 150M, Constantes.EstadosLineaVenta.EN_CURSO, PICKING),
            Linea(2, "SERUM", 1, 99.17M, 120M, Constantes.EstadosLineaVenta.EN_CURSO, PICKING)
        };

        private static LinPedidoVta LineaCupon() => Linea(0, CUPON, -1, -82.64M, -100M, Constantes.EstadosLineaVenta.EN_CURSO, 0, new DateTime(2026, 11, 24));

        private static Dictionary<string, ProductoParaCupon> ProductosCupon(string empresa, List<string> codigos)
            => new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
            {
                ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS" },
                ["SERUM"] = new ProductoParaCupon { Numero = "SERUM", Grupo = "COS" },
                [CUPON] = new ProductoParaCupon { Numero = CUPON, Grupo = "COS", Ficticio = true }
            };

        private static Dictionary<string, ProductoParaChequeRegalo> ProductosCheque(string empresa, List<string> codigos)
            => new Dictionary<string, ProductoParaChequeRegalo>(StringComparer.OrdinalIgnoreCase)
            {
                ["CREMA"] = new ProductoParaChequeRegalo { Numero = "CREMA", Nombre = "CREMA", Grupo = "COS" },
                ["SERUM"] = new ProductoParaChequeRegalo { Numero = "SERUM", Nombre = "SERUM", Grupo = "COS" },
                [CHEQUE] = new ProductoParaChequeRegalo { Numero = CHEQUE, Nombre = "CHEQUE REGALO 50 €", Grupo = "COS", Ficticio = true }
            };

        private static List<CampanaCanjeChequeRegalo> Campanas()
            => new List<CampanaCanjeChequeRegalo> { new CampanaCanjeChequeRegalo { Codigo = "OCT26", Producto = CHEQUE, MinimoCanje = 150M, ImporteBase = 41.32M } };

        private static ResultadoDescuentoAnadidoConPicking Decidir(List<LinPedidoVta> existentes, List<LinPedidoVta> nuevas,
            bool esNotaEntrega = false, bool hayEtiquetaEnCurso = false, Func<int, bool> agencia = null, CuponUsadoPicking cupon = null,
            Func<List<CampanaCanjeChequeRegalo>> campanas = null,
            Func<string, List<string>, Dictionary<string, ProductoParaCupon>> productosCupon = null,
            Func<string, List<string>, Dictionary<string, ProductoParaChequeRegalo>> productosCheque = null)
            => ReglaDescuentoAnadidoConPicking.Decidir("1", 930535, esNotaEntrega, hayEtiquetaEnCurso, existentes, nuevas,
                agencia ?? (p => false), CUPON, cupon ?? new CuponUsadoPicking { Importe = 100M },
                productosCupon ?? ProductosCupon, campanas ?? Campanas, productosCheque ?? ProductosCheque, HOY);

        [TestMethod]
        public void Caso930535_CuponAnadidoConPickingVivoQueLlegaAlMinimo_TomaEsePicking()
        {
            LinPedidoVta cupon = LineaCupon();
            List<LinPedidoVta> existentes = PedidoConPickingVivo();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cupon });
            ReglaDescuentoAnadidoConPicking.Aplicar(resultado);

            Assert.AreEqual(MotivoDescuentoSinPicking.Ninguno, resultado.Motivo);
            Assert.AreEqual(PICKING, resultado.Picking);
            Assert.AreEqual(PICKING, cupon.Picking, "Sale en el mismo albarán y factura que el picking 100155");
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, cupon.Estado);
            Assert.IsTrue(existentes.All(l => l.Picking == PICKING), "Las líneas del picking no se tocan");
        }

        [TestMethod]
        public void CuponPendiente_AlTomarElPicking_PasaAEnCurso()
        {
            LinPedidoVta cupon = LineaCupon();
            cupon.Estado = Constantes.EstadosLineaVenta.PENDIENTE;

            ReglaDescuentoAnadidoConPicking.Aplicar(Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon }));

            Assert.AreEqual(PICKING, cupon.Picking);
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, cupon.Estado);
        }

        [TestMethod]
        public void CuponConPedidoMinimoGuardado_ElPickingNoLlega_SeQuedaSinPickingComoAntes()
        {
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon },
                cupon: new CuponUsadoPicking { Importe = 100M, PedidoMinimo = 300M });
            ReglaDescuentoAnadidoConPicking.Aplicar(resultado);

            Assert.AreEqual(MotivoDescuentoSinPicking.NoLlegaAlMinimo, resultado.Motivo);
            Assert.AreEqual(0, cupon.Picking, "270 € no llegan a 300 €: queda para la siguiente entrega");
        }

        [TestMethod]
        public void CuponConLoYaAlbaranadoAntes_CuentaParaElMinimo()
        {
            List<LinPedidoVta> existentes = PedidoConPickingVivo();
            existentes.Add(Linea(3, "CREMA", 1, 41.32M, 50M, Constantes.EstadosLineaVenta.ALBARAN, 100100));
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cupon },
                cupon: new CuponUsadoPicking { Importe = 100M, PedidoMinimo = 300M });

            Assert.AreEqual(PICKING, resultado.Picking, "50 € ya albaranados + 270 € del picking llegan a 300 €");
        }

        [TestMethod]
        public void PedidoYaAlbaraneado_SinPickingVivo_ComoHoy()
        {
            var existentes = new List<LinPedidoVta>
            {
                Linea(1, "CREMA", 2, 123.97M, 150M, Constantes.EstadosLineaVenta.ALBARAN, PICKING),
                Linea(2, "SERUM", 1, 99.17M, 120M, Constantes.EstadosLineaVenta.ALBARAN, PICKING)
            };
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cupon },
                productosCupon: (e, c) => throw new InvalidOperationException("no debería leer"),
                campanas: () => throw new InvalidOperationException("no debería leer"));
            ReglaDescuentoAnadidoConPicking.Aplicar(resultado);

            Assert.AreEqual(MotivoDescuentoSinPicking.SinPickingVivo, resultado.Motivo);
            Assert.AreEqual(0, cupon.Picking, "Queda para la siguiente entrega, como hoy");
        }

        [TestMethod]
        public void PedidoSinPicking_ComoHoy()
        {
            var existentes = new List<LinPedidoVta> { Linea(1, "CREMA", 2, 123.97M, 150M, Constantes.EstadosLineaVenta.EN_CURSO, 0) };
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cupon });

            Assert.AreEqual(MotivoDescuentoSinPicking.SinPickingVivo, resultado.Motivo);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void ConEtiquetaEnCurso_NoLoToma_ParaNoCambiarElReembolso()
        {
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon }, hayEtiquetaEnCurso: true);

            Assert.AreEqual(MotivoDescuentoSinPicking.EtiquetaEnCurso, resultado.Motivo);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void LaAgenciaYaTieneElPicking_NoLoToma()
        {
            LinPedidoVta cupon = LineaCupon();
            int preguntado = 0;

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon },
                agencia: p => { preguntado = p; return true; });

            Assert.AreEqual(MotivoDescuentoSinPicking.LoTieneLaAgencia, resultado.Motivo);
            Assert.AreEqual(PICKING, preguntado);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void DosPickingsVivos_NoSeElige()
        {
            List<LinPedidoVta> existentes = PedidoConPickingVivo();
            existentes[1].Picking = PICKING + 1;
            LinPedidoVta cupon = LineaCupon();

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cupon });

            Assert.AreEqual(MotivoDescuentoSinPicking.VariosPickingsVivos, resultado.Motivo);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void NotaDeEntrega_NoLoToma()
        {
            LinPedidoVta cupon = LineaCupon();

            Assert.AreEqual(MotivoDescuentoSinPicking.NotaEntrega, Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon }, esNotaEntrega: true).Motivo);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void CuponConFechaPosteriorAlPicking_NoLoToma()
        {
            LinPedidoVta cupon = LineaCupon();
            cupon.Fecha_Entrega = new DateTime(2026, 11, 30);

            Assert.AreEqual(MotivoDescuentoSinPicking.FechaPosterior, Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cupon }).Motivo);
            Assert.AreEqual(0, cupon.Picking);
        }

        [TestMethod]
        public void LineaNegativaQueNoEsCuponNiCheque_NoSeToca()
        {
            LinPedidoVta abono = Linea(0, "CREMA", -1, -61.98M, -75M, Constantes.EstadosLineaVenta.EN_CURSO, 0, HOY);

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { abono });
            ReglaDescuentoAnadidoConPicking.Aplicar(resultado);

            Assert.AreEqual(MotivoDescuentoSinPicking.SinLineasDeDescuento, resultado.Motivo);
            Assert.AreEqual(0, abono.Picking, "Una línea negativa cualquiera sigue sin picking, como hoy");
        }

        [TestMethod]
        public void LineasNuevasPositivas_NiSeLeeNada()
        {
            LinPedidoVta crema = Linea(0, "CREMA", 1, 61.98M, 75M, Constantes.EstadosLineaVenta.EN_CURSO, 0, HOY);

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { crema },
                productosCupon: (e, c) => throw new InvalidOperationException("no debería leer"),
                campanas: () => throw new InvalidOperationException("no debería leer"),
                productosCheque: (e, c) => throw new InvalidOperationException("no debería leer"));

            Assert.AreEqual(MotivoDescuentoSinPicking.SinLineasDeDescuento, resultado.Motivo);
            Assert.AreEqual(0, crema.Picking, "Lo que no es cupón ni cheque lo decide la ampliación en preparación, como antes");
        }

        [TestMethod]
        public void ChequeRegaloAnadidoConPickingVivoQueSuperaElMinimo_TomaEsePicking()
        {
            LinPedidoVta cheque = Linea(0, CHEQUE, -1, -41.32M, -50M, Constantes.EstadosLineaVenta.EN_CURSO, 0, HOY);

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(PedidoConPickingVivo(), new List<LinPedidoVta> { cheque });
            ReglaDescuentoAnadidoConPicking.Aplicar(resultado);

            Assert.AreEqual(PICKING, cheque.Picking, "223,14 € de base superan los 150 € del mínimo de canje");
        }

        [TestMethod]
        public void ChequeRegaloQueNoSuperaElMinimo_SeQuedaSinPicking()
        {
            var existentes = new List<LinPedidoVta> { Linea(1, "CREMA", 1, 61.98M, 75M, Constantes.EstadosLineaVenta.EN_CURSO, PICKING) };
            LinPedidoVta cheque = Linea(0, CHEQUE, -1, -41.32M, -50M, Constantes.EstadosLineaVenta.EN_CURSO, 0, HOY);

            ResultadoDescuentoAnadidoConPicking resultado = Decidir(existentes, new List<LinPedidoVta> { cheque });

            Assert.AreEqual(MotivoDescuentoSinPicking.NoLlegaAlMinimo, resultado.Motivo);
            Assert.AreEqual(0, cheque.Picking);
        }
    }
}
