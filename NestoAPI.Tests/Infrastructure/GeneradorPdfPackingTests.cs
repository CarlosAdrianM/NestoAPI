using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del packing list (migración RDLC -> backend, Nesto#340).
    /// Verifican PDF válido con varios pedidos, con líneas pendientes de servir, lista vacía
    /// y null.
    /// </summary>
    [TestClass]
    public class GeneradorPdfPackingTests
    {
        private GeneradorPdfPacking _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfPacking();
        }

        private static PackingDTO Linea(int pedido, string producto, string tipo = "Servir")
        {
            return new PackingDTO
            {
                Número = pedido,
                NºCliente = "15191",
                Contacto = "0",
                Direccion = "C/ Falsa 123",
                CodPostal = "28001",
                Poblacion = "Madrid",
                Telefono = "912345678",
                Ruta = "AT",
                Usuario = @"NUEVAVISION\Carlos",
                Aviso = "Aviso importante",
                Ampliacion = "Ampliación del pedido",
                ComentarioPicking = "Dejar en portería",
                ProveedorProducto = "612",
                NºProducto = producto,
                CodBarras = "8412345678901",
                Descripcion = "Producto de prueba",
                Tamaño = 50,
                UnidadMedida = "ml",
                NombreSubGrupo = "Cosmética",
                Cantidad = 2,
                CantidadCajas = 1,
                Estado = 1,
                Pasillo = "A",
                Fila = "1",
                Columna = "03",
                Tipo = tipo
            };
        }

        [TestMethod]
        public void GenerarPdf_VariosPedidos_DevuelvePdfValido()
        {
            var lineas = new List<PackingDTO>
            {
                Linea(900001, "38697"),
                Linea(900001, "12345"),
                Linea(900002, "45473")
            };

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ConLineasPendientes_DevuelvePdfValido()
        {
            // Las filas con Tipo = "Pendientes" van en su propia sección dentro del pedido.
            var lineas = new List<PackingDTO>
            {
                Linea(900001, "38697"),
                Linea(900001, "99999", tipo: "Pendientes")
            };

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_MuchasFilasConDescripcionesLargas_NoLanzaPorFilasIndivisibles()
        {
            // #302: las filas llevan ShowEntire para que una descripción de 2-3 líneas que caiga
            // en el corte de página no se parta (duplicaba referencia y cantidad en la hoja
            // siguiente y el operario contaba la unidad dos veces). Este test fuerza varios
            // saltos de página con filas multi-línea y verifica que el layout no lanza.
            var lineas = new List<PackingDTO>();
            for (int i = 0; i < 120; i++)
            {
                PackingDTO linea = Linea(900001, (10000 + i).ToString());
                linea.Descripcion = "DESCRIPCIÓN LARGUÍSIMA DE PRODUCTO QUE OCUPA VARIAS LÍNEAS EN LA CELDA " +
                    "PORQUE EL NOMBRE COMERCIAL INCLUYE GAMA, VARIANTE, TAMAÑO Y APELLIDOS " + i;
                lineas.Add(linea);
            }

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(123456, new List<PackingDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(123456, null);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        // ----- Estructura del informe (caso real: picking 98900, 13/07/26) -----
        // El RDLC agrupa por CLIENTE (salto de página entre clientes, no por pedido), con los
        // pedidos del cliente en secuencia y TODAS sus pendientes en una única sección al final.
        // La primera versión QuestPDF agrupaba por pedido y salía en 3 páginas con el pedido
        // 907562 (todo pendientes) como bloque propio. Estos tests fijan la estructura correcta.

        private static List<PackingDTO> LineasPicking98900()
        {
            return new List<PackingDTO>
            {
                // Pedido 907562: SOLO tiene una línea pendiente (no debe tener bloque propio).
                Linea(907562, "38819", tipo: "Pendientes"),
                // Pedido 922166: líneas a servir (la primera SIN ruta ni usuario, como el SP real)
                // y una pendiente.
                SinRutaNiUsuario(Linea(922166, "42973")),
                Linea(922166, "17404"),
                Linea(922166, "39288", tipo: "Pendientes"),
                // Pedido 920569: líneas a servir.
                Linea(920569, "41149"),
                Linea(920569, "39288")
            };
        }

        private static PackingDTO SinRutaNiUsuario(PackingDTO linea)
        {
            linea.Ruta = null;
            linea.Usuario = "  ";
            return linea;
        }

        [TestMethod]
        public void Agrupar_MismoClienteYDireccion_UnSoloBloque()
        {
            var bloques = GeneradorPdfPacking.Agrupar(LineasPicking98900());

            Assert.AreEqual(1, bloques.Count, "Todos los pedidos del mismo cliente/dirección van en UN bloque (una página)");
            Assert.AreEqual("15191", bloques[0].Cliente);
        }

        [TestMethod]
        public void Agrupar_PedidoSoloConPendientes_NoTieneBloquePropio()
        {
            var bloques = GeneradorPdfPacking.Agrupar(LineasPicking98900());

            CollectionAssert.AreEqual(new[] { 920569, 922166 },
                bloques[0].Pedidos.Select(p => p.Numero).ToArray(),
                "Solo los pedidos con líneas a servir tienen bloque, ordenados por número; el 907562 (todo pendientes) no");
        }

        [TestMethod]
        public void Agrupar_LasPendientesDeTodosLosPedidos_VanJuntasAlFinalDelCliente()
        {
            var bloques = GeneradorPdfPacking.Agrupar(LineasPicking98900());

            CollectionAssert.AreEqual(new[] { "38819", "39288" },
                bloques[0].Pendientes.Select(p => p.NºProducto).ToArray(),
                "Las pendientes del 907562 y del 922166 van en una única sección, ordenadas por pedido");
            Assert.AreEqual(2, bloques[0].Pedidos.Single(p => p.Numero == 922166).Lineas.Count,
                "Las pendientes NO se mezclan con las líneas a servir del pedido");
        }

        [TestMethod]
        public void Agrupar_RutaYUsuarioVacios_SeCogeElPrimerValorNoVacio()
        {
            // El SP no rellena Ruta/Usuario en todas las filas: si la primera del pedido viene
            // vacía, se toma de otra línea (del pedido o del bloque). Era el 'Ruta:' en blanco
            // del 922166 en la primera versión.
            var bloques = GeneradorPdfPacking.Agrupar(LineasPicking98900());

            var pedido922166 = bloques[0].Pedidos.Single(p => p.Numero == 922166);
            Assert.AreEqual("AT", pedido922166.Ruta);
            StringAssert.Contains(pedido922166.Usuario, "Carlos");
        }

        [TestMethod]
        public void Agrupar_ClientesDistintos_BloquesSeparados()
        {
            var lineas = LineasPicking98900();
            var otroCliente = Linea(930000, "12345");
            otroCliente.NºCliente = "22222";
            lineas.Add(otroCliente);

            var bloques = GeneradorPdfPacking.Agrupar(lineas);

            Assert.AreEqual(2, bloques.Count, "Cada cliente/dirección es un bloque (salto de página entre ellos)");
        }

        [TestMethod]
        public void Agrupar_BloquesOrdenadosPorElPedidoDeSuPrimeraFila()
        {
            // #293: el RDLC ordena el grupo Cliente por Fields!Número.Value (el pedido de la
            // primera fila del bloque). Sin replicarlo, los bloques salen por orden de aparición
            // en el SP y no se puede comparar hoja a hoja con el informe viejo.
            var tardio = Linea(930000, "12345");
            tardio.NºCliente = "22222";
            var temprano = Linea(910000, "67890");
            temprano.NºCliente = "11111";
            var lineas = new List<PackingDTO> { tardio, temprano };

            var bloques = GeneradorPdfPacking.Agrupar(lineas);

            CollectionAssert.AreEqual(new[] { "11111", "22222" },
                bloques.Select(b => b.Cliente).ToArray(),
                "El bloque cuyo primer pedido es menor va antes, aunque el SP lo devuelva después");
        }

        [TestMethod]
        public void Agrupar_AmpliacionSoloEnFilaPosterior_NoSeMuestra()
        {
            // #293 (caso real 922172): el RDLC muestra la Ampliacion de la PRIMERA fila del
            // pedido (semántica First de un textbox de grupo). Con primer-valor-no-vacío, una
            // fila suelta con texto marcaba 'AMPLIACIÓN PEDIDO' en pedidos que el viejo no marcaba.
            var primera = Linea(922172, "27593");
            primera.Ampliacion = null;
            var posterior = Linea(922172, "12345");
            posterior.Ampliacion = "AMPLIACIÓN PEDIDO";
            var lineas = new List<PackingDTO> { primera, posterior };

            var bloques = GeneradorPdfPacking.Agrupar(lineas);

            Assert.IsNull(bloques[0].Pedidos.Single().Ampliacion,
                "Si la primera fila del pedido no trae ampliación, el informe no la muestra (como el RDLC)");
        }

        [TestMethod]
        public void Agrupar_AmpliacionEnPrimeraFila_SeMuestra()
        {
            var primera = Linea(922172, "27593");
            primera.Ampliacion = "AMPLIACIÓN PEDIDO";
            var posterior = Linea(922172, "12345");
            posterior.Ampliacion = null;
            var lineas = new List<PackingDTO> { primera, posterior };

            var bloques = GeneradorPdfPacking.Agrupar(lineas);

            Assert.AreEqual("AMPLIACIÓN PEDIDO", bloques[0].Pedidos.Single().Ampliacion);
        }

        // Los PDF empiezan por la firma "%PDF" (0x25 0x50 0x44 0x46).
        private static void ComprobarCabeceraPdf(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length >= 4, "El PDF es demasiado corto");
            Assert.AreEqual(0x25, bytes[0]);
            Assert.AreEqual(0x50, bytes[1]);
            Assert.AreEqual(0x44, bytes[2]);
            Assert.AreEqual(0x46, bytes[3]);
        }
    }
}
