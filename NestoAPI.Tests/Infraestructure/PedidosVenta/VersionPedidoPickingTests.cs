using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Carlos 26/11/26: Almacén sacó el picking y Nesto dijo «Carlos ha modificado este pedido a las 10:58» (Usuario y Fecha eran los de
    /// quien lo creó: sacar el picking cambia la RowVersion de las líneas pero no quién ni cuándo). Pickings + HuellaContenido en la
    /// versión y el aviso «pedidoCambiado» con Motivo PickingSacado/PickingQuitado desde el punto único que saca o quita el picking.
    /// </summary>
    [TestClass]
    public class VersionPedidoPickingTests
    {
        private Func<IAvisosPedidoCambiado> avisosAntes;
        private Func<string, int, bool> hayAlguienAntes;
        private Func<string, int, DatosVersionPedido> leerAntes;
        private Action<Action> hiloAntes;
        private List<PedidoCambiadoDTO> mandados;
        private List<int> leidos;
        private bool alguienMirando;

        private class AvisosFalsos : IAvisosPedidoCambiado
        {
            private readonly List<PedidoCambiadoDTO> lista;
            private readonly ManualResetEventSlim hecho;

            public AvisosFalsos(List<PedidoCambiadoDTO> lista, ManualResetEventSlim hecho = null)
            {
                this.lista = lista;
                this.hecho = hecho;
            }

            public void PedidoCambiado(PedidoCambiadoDTO aviso)
            {
                lock (lista)
                {
                    lista.Add(aviso);
                }
                hecho?.Set();
            }
        }

        private static DatosVersionPedido Datos(string pickings = null, byte ultimoByteLineas = 9)
        {
            return new DatosVersionPedido
            {
                RowVersionCabecera = new byte[] { 0, 0, 0, 0, 0, 1, 2, 3 },
                UsuarioCabecera = "NUEVAVISION\\Carlos",
                FechaCabecera = new DateTime(2026, 11, 26, 10, 58, 0),
                Lineas = 3,
                RowVersionLineas = new byte[] { 0, 0, 0, 0, 0, 1, 2, ultimoByteLineas },
                UsuarioLinea = "NUEVAVISION\\Carlos",
                FechaLinea = new DateTime(2026, 11, 26, 10, 58, 0),
                PickingsLineas = pickings,
                HuellaCabeceraContenido = 12345,
                HuellaLineasContenido = 67890
            };
        }

        [TestInitialize]
        public void Preparar()
        {
            avisosAntes = AvisoPedidoCambiado.Avisos;
            hayAlguienAntes = AvisoPedidoCambiado.HayAlguienConElPedido;
            leerAntes = AvisoPedidoCambiado.LeerDatos;
            hiloAntes = AvisoPedidoCambiado.EnOtroHilo;
            mandados = new List<PedidoCambiadoDTO>();
            leidos = new List<int>();
            alguienMirando = true;
            AvisoPedidoCambiado.Avisos = () => new AvisosFalsos(mandados);
            AvisoPedidoCambiado.HayAlguienConElPedido = (e, p) => alguienMirando;
            AvisoPedidoCambiado.LeerDatos = (e, p) => { leidos.Add(p); return Datos("100198"); };
            AvisoPedidoCambiado.EnOtroHilo = accion => accion();
        }

        [TestCleanup]
        public void Restaurar()
        {
            AvisoPedidoCambiado.Avisos = avisosAntes;
            AvisoPedidoCambiado.HayAlguienConElPedido = hayAlguienAntes;
            AvisoPedidoCambiado.LeerDatos = leerAntes;
            AvisoPedidoCambiado.EnOtroHilo = hiloAntes;
        }

        [TestMethod]
        public void SacarElPicking_CambiaLaVersionPeroNoLaHuellaDelContenido()
        {
            DatosVersionPedido antes = Datos(null, 9);
            DatosVersionPedido despues = Datos("100198", 10); // la RowVersion de las líneas sube; el contenido es el mismo

            Assert.AreNotEqual(VersionPedidoVenta.Calcular(antes), VersionPedidoVenta.Calcular(despues), "la Version cambia (decide el 409, como siempre)");
            Assert.AreEqual(VersionPedidoVenta.HuellaContenido(antes), VersionPedidoVenta.HuellaContenido(despues), "el contenido, no");
            Assert.AreEqual(32, VersionPedidoVenta.HuellaContenido(antes).Length);
        }

        [TestMethod]
        public void HuellaContenido_CambiaConLasLineasLaCabeceraLosPrepagosYLosEfectos()
        {
            string inicial = VersionPedidoVenta.HuellaContenido(Datos());
            DatosVersionPedido lineas = Datos(); lineas.HuellaLineasContenido = 1;
            DatosVersionPedido cabecera = Datos(); cabecera.HuellaCabeceraContenido = 1;
            DatosVersionPedido unaLineaMenos = Datos(); unaLineaMenos.Lineas = 2;
            DatosVersionPedido prepago = Datos(); prepago.Prepagos = 1; prepago.SumaIdPrepagos = 77; prepago.HuellaPrepagos = 5;
            DatosVersionPedido efecto = Datos(); efecto.Efectos = 1; efecto.SumaIdEfectos = 8; efecto.HuellaEfectos = 3;

            foreach (DatosVersionPedido d in new[] { lineas, cabecera, unaLineaMenos, prepago, efecto })
            {
                Assert.AreNotEqual(inicial, VersionPedidoVenta.HuellaContenido(d));
            }
            Assert.IsNull(VersionPedidoVenta.HuellaContenido(null));
        }

        [TestMethod]
        public void Pickings_OrdenadosSinRepetirNiCeros()
        {
            CollectionAssert.AreEqual(new[] { 100195, 100198 }, VersionPedidoVenta.Pickings(Datos("100198,100195,0,100198")));
            Assert.AreEqual(0, VersionPedidoVenta.Pickings(Datos(null)).Count, "sin picking, lista vacía");
            Assert.IsNull(VersionPedidoVenta.Pickings(null), "sin datos, null");
        }

        [TestMethod]
        public void LaConsulta_EsUnaSolaIdaYSinFuncionesQueLaBdNoTiene()
        {
            string sql = LectorVersionPedidoSql.SQL;
            StringAssert.Contains(sql, "FOR XML PATH('')");
            StringAssert.Contains(sql, "AS PickingsLineas");
            StringAssert.Contains(sql, "AS HuellaCabeceraContenido");
            StringAssert.Contains(sql, "AS HuellaLineasContenido");
            Assert.IsFalse(sql.Contains("STRING_AGG") || sql.Contains("STRING_SPLIT") || sql.Contains("TRY_CONVERT"));
            Assert.IsFalse(sql.Contains("NOLOCK"));
            // Las huellas del contenido no miran el picking ni el paso de pendiente a en curso
            int desde = sql.IndexOf("CHECKSUM_AGG(BINARY_CHECKSUM(x.[Nº Orden]", StringComparison.Ordinal);
            int hasta = sql.IndexOf("AS HuellaLineasContenido", StringComparison.Ordinal);
            string huellaLineas = sql.Substring(desde, hasta - desde);
            Assert.IsFalse(huellaLineas.Contains("Picking"));
            StringAssert.Contains(huellaLineas, "CASE WHEN x.Estado < 2 THEN 0 ELSE x.Estado END");
            int cabecera = sql.IndexOf("AS HuellaCabeceraContenido", StringComparison.Ordinal);
            string huellaCabecera = sql.Substring(sql.LastIndexOf("BINARY_CHECKSUM(c.", cabecera, StringComparison.Ordinal),
                cabecera - sql.LastIndexOf("BINARY_CHECKSUM(c.", cabecera, StringComparison.Ordinal));
            Assert.IsFalse(huellaCabecera.Contains("Usuario") || huellaCabecera.Contains("Modificación") || huellaCabecera.Contains("RowVersion"));
        }

        [TestMethod]
        public void PorPicking_ConElPedidoAbierto_MandaQuienYPorQueConLosPickings()
        {
            AvisoPedidoCambiado.PorPicking("1  ", 931700, "NUEVAVISION\\Andre", AvisoPedidoCambiado.MOTIVO_PICKING_SACADO);

            PedidoCambiadoDTO aviso = mandados.Single();
            Assert.AreEqual("1", aviso.Empresa);
            Assert.AreEqual(931700, aviso.Pedido);
            Assert.AreEqual("Andre", aviso.Usuario);
            Assert.AreEqual("PickingSacado", aviso.Motivo);
            CollectionAssert.AreEqual(new[] { 100198 }, aviso.Pickings);
            Assert.AreEqual(VersionPedidoVenta.Calcular(Datos("100198")), aviso.Version);
            Assert.AreEqual(VersionPedidoVenta.HuellaContenido(Datos("100198")), aviso.HuellaContenido);
        }

        [TestMethod]
        public void PorPicking_SinNadieConElPedido_NiLeeNiManda()
        {
            alguienMirando = false;

            AvisoPedidoCambiado.PorPicking("1", 931700, "Andre", AvisoPedidoCambiado.MOTIVO_PICKING_QUITADO);

            Assert.AreEqual(0, leidos.Count);
            Assert.AreEqual(0, mandados.Count);
        }

        [TestMethod]
        public void PorPicking_SiFallaLaLectura_ElAvisoSaleIgualYNuncaLanza()
        {
            AvisoPedidoCambiado.LeerDatos = (e, p) => throw new InvalidOperationException("sin BD");

            AvisoPedidoCambiado.PorPicking("1", 931700, "Andre", AvisoPedidoCambiado.MOTIVO_PICKING_QUITADO);

            Assert.AreEqual("PickingQuitado", mandados.Single().Motivo);
            Assert.IsNull(mandados.Single().Version);
        }

        [TestMethod]
        public void EnSegundoPlano_ConLosDatosDelGuardado_LlevaPickingsYHuellaSinMotivo()
        {
            var hecho = new ManualResetEventSlim(false);
            AvisoPedidoCambiado.Avisos = () => new AvisosFalsos(mandados, hecho);

            AvisoPedidoCambiado.EnSegundoPlano("1", 931700, "NUEVAVISION\\Aida", "v1", Datos("100195"));

            Assert.IsTrue(hecho.Wait(5000));
            PedidoCambiadoDTO aviso = mandados.Single();
            Assert.AreEqual("v1", aviso.Version, "la versión ya calculada manda");
            CollectionAssert.AreEqual(new[] { 100195 }, aviso.Pickings);
            Assert.IsNotNull(aviso.HuellaContenido);
            Assert.IsNull(aviso.Motivo);
        }

        [TestMethod]
        public void GestorPicking_AvisaUnaVezPorPedidoYSinUsuarioDiceAlmacen()
        {
            var pedidos = new List<PedidoPicking>
            {
                new PedidoPicking { Empresa = "1", Id = 931700 },
                new PedidoPicking { Empresa = "1", Id = 931700 },
                new PedidoPicking { Empresa = "1", Id = 931701 }
            };

            GestorPicking.AvisarPickingSacado(pedidos, null);

            CollectionAssert.AreEquivalent(new[] { 931700, 931701 }, mandados.Select(m => m.Pedido).ToArray());
            Assert.IsTrue(mandados.All(m => m.Usuario == GestorPicking.USUARIO_POR_DEFECTO && m.Motivo == AvisoPedidoCambiado.MOTIVO_PICKING_SACADO));
        }
    }
}
