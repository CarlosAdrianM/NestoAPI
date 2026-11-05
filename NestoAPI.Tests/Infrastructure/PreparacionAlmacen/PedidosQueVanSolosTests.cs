using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Informes;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#825 (sugerencia 1056, Santiago; Carlos 05/11/26): «En los clientes que usan factura simplificada, cada pedido puede
    /// ser para un cliente final diferente (de hecho las etiquetas de agencia son para destinos diferentes). Tenemos que manejarlos
    /// como tal». Cada pedido de esos clientes es su propia entrega (packing de Ariadna, sus cajas, el packing en papel y su parada);
    /// los clientes reales, como siempre (mismo cliente y contacto = misma entrega).
    /// </summary>
    [TestClass]
    public class PedidosQueVanSolosTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 99700;
        private const string AMAZON = Constantes.ClientesEspeciales.AMAZON;
        private const string REAL = "40182";

        private static FilaPackingAlmacen Fila(int pedido, string cliente, string contacto = "0", string producto = "A", int linea = 1)
            => new FilaPackingAlmacen
            {
                Pedido = pedido,
                LineaPedido = linea,
                Producto = producto,
                Descripcion = "PRODUCTO " + producto,
                CodigoBarras = "8437020512176",
                Cantidad = 1,
                Cliente = cliente,
                Contacto = contacto,
                Nombre = "CLIENTE " + cliente,
                Ruta = "FW "
            };

        // ================================================================ la regla

        [TestMethod]
        public void PedidoVaSolo_LosDeFacturaSimplificada_SiYLosRealesNo()
        {
            foreach (string cliente in new[] { "32624", "31517", "10458", "9500", "31794", "32624  ", " 10458" })
            {
                Assert.IsTrue(PedidosQueVanSolos.PedidoVaSolo(cliente), cliente);
            }
            foreach (string cliente in new[] { REAL, Constantes.ClientesEspeciales.EL_EDEN, null, "", "   " })
            {
                Assert.IsFalse(PedidosQueVanSolos.PedidoVaSolo(cliente), cliente ?? "null");
            }
            Assert.IsTrue(PedidosQueVanSolos.PedidoVaSolo(new CabPedidoVta { Nº_Cliente = "31517     " }));
            Assert.IsFalse(PedidosQueVanSolos.PedidoVaSolo((CabPedidoVta)null));
        }

        [TestMethod]
        public void PedidoVaSolo_EsLaMismaListaQueLasFacturasSimplificadas()
        {
            foreach (string cliente in Constantes.ClientesEspeciales.ClientesFacturaSimplificada)
            {
                Assert.IsTrue(PedidosQueVanSolos.PedidoVaSolo(cliente), cliente);
                StringAssert.Contains(PedidosQueVanSolos.CondicionSql("c.[Nº Cliente]"), "'" + cliente + "'", "El SQL sale de la misma lista");
            }
        }

        [TestMethod]
        public void ClaveSolo_ElPedidoSiVaSolo_YCeroSiSeAgrupa()
        {
            Assert.AreEqual(930001, PedidosQueVanSolos.ClaveSolo(AMAZON, 930001));
            Assert.AreEqual(0, PedidosQueVanSolos.ClaveSolo(REAL, 930001));
            Assert.AreEqual("CASE WHEN RTRIM(@cliente) IN ('10458', '31517', '31794', '32624', '9500') THEN @p2 ELSE 0 END",
                PedidosQueVanSolos.ClaveSoloSql("@cliente", "@p2"));
        }

        // ================================================================ el packing de Ariadna

        [TestMethod]
        public void MontarPacking_DosPedidosDeAmazonDelMismoCliente_DosEntregas()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(930001, AMAZON, producto: "A"),
                Fila(930001, AMAZON, producto: "B", linea: 2),
                Fila(930002, AMAZON, producto: "A")
            });

            Assert.AreEqual(2, packing.Entregas.Count, "Cada pedido de Amazon va a una persona distinta: su propia entrega");
            CollectionAssert.AreEqual(new[] { 930001 }, packing.Entregas[0].Pedidos.Select(p => p.Pedido).ToArray());
            CollectionAssert.AreEqual(new[] { 930002 }, packing.Entregas[1].Pedidos.Select(p => p.Pedido).ToArray());
            Assert.AreEqual(2, packing.Entregas[0].Pedidos.Single().Lineas.Count);
            Assert.IsTrue(packing.Entregas.All(e => e.Cliente == AMAZON && e.Contacto == "0"));
        }

        [TestMethod]
        public void MontarPacking_DosPedidosDeUnClienteRealALaMismaDireccion_UnaEntrega()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(930010, REAL),
                Fila(930011, REAL)
            });

            Assert.AreEqual(1, packing.Entregas.Count, "Como hoy: mismo cliente y contacto = misma entrega");
            CollectionAssert.AreEqual(new[] { 930010, 930011 }, packing.Entregas.Single().Pedidos.Select(p => p.Pedido).ToArray());
        }

        [TestMethod]
        public void MontarPacking_Mezcla_SoloSeJuntanLosDelClienteReal()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(930001, AMAZON),
                Fila(930002, AMAZON),
                Fila(930003, Constantes.ClientesEspeciales.TIENDA_ONLINE),
                Fila(930010, REAL),
                Fila(930011, REAL),
                Fila(930012, REAL, contacto: "1")
            });

            Assert.AreEqual(5, packing.Entregas.Count);
            CollectionAssert.AreEquivalent(new[] { 930010, 930011 },
                packing.Entregas.Single(e => e.Cliente == REAL && e.Contacto == "0").Pedidos.Select(p => p.Pedido).ToArray());
            Assert.IsTrue(packing.Entregas.Where(e => e.Cliente != REAL).All(e => e.Pedidos.Count == 1));
        }

        [TestMethod]
        public void MontarPacking_CodigoDuplicado_SoloDentroDeSuPedido()
        {
            // Dos pedidos de Amazon con productos distintos y el mismo código: ya no son la misma entrega, no hay duplicado
            FilaPackingAlmacen uno = Fila(930001, AMAZON, producto: "A");
            FilaPackingAlmacen otro = Fila(930002, AMAZON, producto: "B");

            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[] { uno, otro });

            Assert.IsTrue(packing.Entregas.SelectMany(e => e.Pedidos).SelectMany(p => p.Lineas).All(l => !l.CodigoDuplicado));
        }

        [TestMethod]
        public void BultosYEtiqueta_DeUnPedidoDeAmazon_SoloLosSuyosYSinOtrosPedidos()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
            {
                Fila(930001, AMAZON),
                Fila(930002, AMAZON)
            });
            ServicioPreparacionAlmacen.RepartirBultosYActividad(packing, new[]
            {
                new BultoAlmacenDTO { Picking = PICKING, Pedido = 930001, Bulto = 1, RutaBlob = "a.jpg", TieneFoto = true },
                new BultoAlmacenDTO { Picking = PICKING, Pedido = 930002, Bulto = 1, RutaBlob = "b.jpg", TieneFoto = true },
                new BultoAlmacenDTO { Picking = PICKING, Pedido = 930002, Bulto = 2, RutaBlob = "c.jpg", TieneFoto = true }
            }, null);

            Assert.AreEqual(1, packing.Entregas[0].Bultos.Count, "La caja 2 del otro pedido no es de esta entrega");
            Assert.AreEqual(2, packing.Entregas[1].Bultos.Count);

            EntregaParaEtiqueta etiqueta = EntregaParaEtiqueta.De(packing, 930001);
            Assert.AreEqual(1, etiqueta.Bultos, "La etiqueta lleva solo las cajas de su pedido");
            Assert.AreEqual(0, etiqueta.OtrosPedidos.Count, "Sin «En la misma entrega van también…»");
        }

        [TestMethod]
        public void SqlEntregasPorEmpaquetar_AgrupaConLaClaveSolo()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_ENTREGAS_POR_EMPAQUETAR;
            StringAssert.Contains(sql, PedidosQueVanSolos.ClaveSoloSql("c.[Nº Cliente]", "l.[Número]") + " AS Solo");
            StringAssert.Contains(sql, "GROUP BY Picking, Cliente, Contacto, Solo");
            Assert.AreEqual(3, CuantasVeces(sql, "li.Contacto = e.Contacto AND li.Solo = e.Solo"),
                "Bultos con foto, sin foto y pedidos con etiqueta: de la misma entrega");
            Assert.AreEqual(0, CuantasVeces(sql, "li.Contacto = e.Contacto)"));
            StringAssert.Contains(RepositorioPreparacionAlmacen.SqlEntregasPorEmpaquetar(true), "f.Pedido IN (SELECT li.Pedido FROM lineas li WHERE li.Picking = e.Picking AND li.Cliente = e.Cliente AND li.Contacto = e.Contacto AND li.Solo = e.Solo)");
        }

        [TestMethod]
        public void SqlGuardarBultoSinPisar_ElPedidoQueVaSolo_NoCompartePasoNiNumeracion()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_GUARDAR_BULTO_SIN_PISAR;
            StringAssert.Contains(sql, "DECLARE @solo int = " + PedidosQueVanSolos.ClaveSoloSql("@cliente", "@p2") + ";");
            StringAssert.Contains(sql, "N':', @solo);", "El cerrojo es del pedido que va solo");
            StringAssert.Contains(sql, "b.Pedido = @p2 OR (@solo = 0 AND b.Pedido IN (SELECT c.[Número] FROM CabPedidoVta c");
            Assert.IsTrue(sql.IndexOf("DECLARE @solo") < sql.IndexOf("DECLARE @recurso"));
        }

        private static int CuantasVeces(string texto, string trozo)
        {
            int n = 0;
            for (int i = texto.IndexOf(trozo, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(trozo, i + 1, StringComparison.Ordinal))
            {
                n++;
            }
            return n;
        }

        // ================================================================ el packing en papel

        private static PackingDTO LineaPapel(int pedido, string cliente, string tipo = null, string producto = "A")
            => new PackingDTO
            {
                Número = pedido,
                NºCliente = cliente,
                Contacto = "0",
                Direccion = "C/ DE LA FICHA, 1",
                NºProducto = producto,
                Cantidad = 1,
                Tipo = tipo
            };

        [TestMethod]
        public void PackingEnPapel_DosPedidosDeVentaTienda_DosHojas_YElClienteRealUna()
        {
            List<BloqueClientePacking> bloques = GeneradorPdfPacking.Agrupar(new List<PackingDTO>
            {
                LineaPapel(930001, Constantes.ClientesEspeciales.PUBLICO_FINAL),
                LineaPapel(930002, Constantes.ClientesEspeciales.PUBLICO_FINAL),
                LineaPapel(930010, REAL),
                LineaPapel(930011, REAL)
            });

            Assert.AreEqual(3, bloques.Count);
            Assert.AreEqual(2, bloques.Count(b => b.Cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL && b.Pedidos.Count == 1));
            CollectionAssert.AreEqual(new[] { 930010, 930011 }, bloques.Single(b => b.Cliente == REAL).Pedidos.Select(p => p.Numero).ToArray());
        }

        [TestMethod]
        public void PackingEnPapel_PendientesDeUnPedidoQueVaSolo_ConSuPedido_YLosDeOtroPedidoFuera()
        {
            List<BloqueClientePacking> bloques = GeneradorPdfPacking.Agrupar(new List<PackingDTO>
            {
                LineaPapel(930001, Constantes.ClientesEspeciales.PUBLICO_FINAL),
                LineaPapel(930001, Constantes.ClientesEspeciales.PUBLICO_FINAL, "Pendientes", "B"),
                LineaPapel(930099, Constantes.ClientesEspeciales.PUBLICO_FINAL, "Pendientes", "C"),
                LineaPapel(930010, REAL),
                LineaPapel(930098, REAL, "Pendientes", "D")
            });

            Assert.AreEqual(2, bloques.Count, "El pendiente de otro pedido de venta tienda no es de nadie del picking");
            BloqueClientePacking tienda = bloques.Single(b => b.Cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL);
            Assert.AreEqual("B", tienda.Pendientes.Single().NºProducto);
            Assert.AreEqual("D", bloques.Single(b => b.Cliente == REAL).Pendientes.Single().NºProducto, "El cliente real, como siempre");
        }

        // ================================================================ Facturar rutas y el reparto propio

        [TestMethod]
        public async Task FacturarRutas_DosNotasDeVentaTiendaPorRutaPropia_DosEnvios()
        {
            RutasPropiasConfiguradas.Reiniciar();
            try
            {
                var paradas = new ParadasPorPedidoFalso();
                var servicio = new ServicioParadasNotaEntrega(paradas, new CabecerasFalsas(), () => true, _ => { }, () => new DateTime(2026, 11, 5));

                ResultadoParadaNotaEntrega uno = await servicio.AsegurarParada(Nota(930001), "alfredo");
                ResultadoParadaNotaEntrega dos = await servicio.AsegurarParada(Nota(930002), "alfredo");

                Assert.AreEqual(AccionParadaNotaEntrega.Creada, uno.Accion);
                Assert.AreEqual(AccionParadaNotaEntrega.Creada, dos.Accion, "El segundo pedido no se mete en la parada del primero");
                Assert.AreNotEqual(uno.EnvioId, dos.EnvioId);
                CollectionAssert.AreEqual(new int?[] { 930001, 930002 }, paradas.Creados.Select(e => e.Pedido).ToArray());
            }
            finally
            {
                RutasPropiasConfiguradas.Reiniciar();
            }
        }

        private static CabPedidoVta Nota(int numero) => new CabPedidoVta
        {
            Empresa = EMPRESA,
            Número = numero,
            Nº_Cliente = Constantes.ClientesEspeciales.PUBLICO_FINAL,
            Contacto = "0",
            Ruta = "AT",
            NotaEntrega = true
        };

        /// <summary>Los envíos de cada pedido por separado, como la BD (EnviosAgencia.Pedido).</summary>
        private class ParadasPorPedidoFalso : IRepositorioParadasNotaEntrega
        {
            public readonly List<EnviosAgencia> Creados = new List<EnviosAgencia>();
            private int siguiente = 270000;

            public Task<List<EnvioPedidoNota>> LeerEnviosDelPedido(int pedido)
                => Task.FromResult(Creados.Where(e => e.Pedido == pedido)
                    .Select(e => new EnvioPedidoNota { Numero = e.Numero, Agencia = e.Agencia, Estado = e.Estado, Bultos = e.Bultos }).ToList());

            public Task<EnviosAgencia> LeerEnvio(int numero) => Task.FromResult(Creados.FirstOrDefault(e => e.Numero == numero));

            public Task<DestinoNotaEntrega> LeerDestinoDelPedido(string empresa, int pedido)
                => Task.FromResult(new DestinoNotaEntrega
                {
                    Cliente = Constantes.ClientesEspeciales.PUBLICO_FINAL, Contacto = "0", Nombre = "VENTA TIENDA", Direccion = "C/ DE LA FICHA, 1",
                    CodPostal = "28110", Poblacion = "ALGETE", Provincia = "MADRID"
                });

            public Task<string> LeerEmpresaAgencia(int agencia) => Task.FromResult(EMPRESA);

            public Task<int> CrearEnvioTramitado(EnviosAgencia envio, string historia)
            {
                envio.Numero = siguiente++;
                Creados.Add(envio);
                return Task.FromResult(envio.Numero);
            }
        }

        private class CabecerasFalsas : IRepositorioNotasEntregaCabecera
        {
            public bool ScriptAplicado() => true;
            public Task<FilaNotaEntregaCabecera> Leer(string empresa, int pedido) => Task.FromResult<FilaNotaEntregaCabecera>(null);
            public Task Guardar(FilaNotaEntregaCabecera fila, Modificacion modificacion) => Task.CompletedTask;
            public Task<PedidoNotaEntrega> LeerPedido(string empresa, int pedido) => Task.FromResult<PedidoNotaEntrega>(null);
            public Task<int?> LeerNumeroNota(string empresa, int pedido) => Task.FromResult<int?>(null);
            public Task<bool> TieneLineasProcesadas(string empresa, int pedido) => Task.FromResult(false);
            public Task<EnvioNotaEntrega> LeerUltimoEnvio(string empresa, int pedido) => Task.FromResult<EnvioNotaEntrega>(null);
            public Task<string> LeerClienteFactura(string empresa, string factura) => Task.FromResult<string>(null);
        }
    }
}
