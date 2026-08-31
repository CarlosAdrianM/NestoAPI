using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#422: red que se pone sola. Cada campo nuevo del mensaje de Productos habia que
    /// acordarse de mapearlo, y olvidarse no daba ningun error: el producto simplemente viajaba
    /// sin ese dato. Estos tests recorren el DTO por reflexion y fallan solos cuando aparece un
    /// campo que no llega al mensaje.
    ///
    /// Si añades una propiedad al DTO que NO deba viajar, metela en NoViajanAProposito con su
    /// motivo. Es una decision explicita, no un olvido.
    /// </summary>
    [TestClass]
    public class ProductoSyncMessageCompletoTests
    {
        private static readonly Dictionary<string, string> NoViajanAProposito = new Dictionary<string, string>
        {
            ["SubgrupoCodigo"] =
                "Nesto#456: el codigo del subgrupo es para la ficha de Nesto. En el bus, Subgrupo " +
                "viaja como DESCRIPCION y PrestaShop y Odoo lo consumen asi; meter el codigo sin " +
                "avisarles cambiaria el contrato.",
            ["FamiliaCodigo"] =
                "NestoAPI#423: mismo caso y mismo criterio que SubgrupoCodigo. El codigo de la " +
                "familia esta en el DTO para quien consume la API (la ficha de Nesto, y de paso " +
                "para no volver a confundir el codigo con la descripcion al buscar filas de " +
                "DescuentosProducto). Por el bus, Familia viaja como DESCRIPCION: si PrestaShop u " +
                "Odoo llegan a necesitar el codigo para identificar la marca, se anade al mensaje " +
                "DESPUES de acordarlo con ellos, no por sorpresa."
        };

        [TestMethod]
        public void TodoCampoDelDTO_OViajaEnElMensaje_OEstaDocumentadoQueNo()
        {
            var propiedadesMensaje = typeof(ProductoSyncMessage)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var huerfanas = typeof(ProductoDTO)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => p.Name)
                .Where(nombre => !propiedadesMensaje.Contains(nombre))
                .Where(nombre => !NoViajanAProposito.ContainsKey(nombre))
                .ToList();

            Assert.AreEqual(0, huerfanas.Count,
                "Estos campos del ProductoDTO no llegan al mensaje. O se mapean en " +
                "GestorProductos, o se documentan en NoViajanAProposito: " +
                string.Join(", ", huerfanas));
        }

        [TestMethod]
        public void LasExcepcionesDocumentadas_SiguenSiendoCamposDeVerdad()
        {
            // Que la lista de excepciones no se quede con nombres de campos que ya no existen: un
            // campo renombrado se colaria sin viajar y con la excusa de una excepcion caducada.
            var propiedadesDto = typeof(ProductoDTO)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var caducadas = NoViajanAProposito.Keys.Where(n => !propiedadesDto.Contains(n)).ToList();

            Assert.AreEqual(0, caducadas.Count,
                "Excepciones que ya no corresponden a ningun campo del DTO: " + string.Join(", ", caducadas));
        }

        [TestMethod]
        public async Task TodoCampoEscalarDelDTO_LlegaConSuValorAlMensaje()
        {
            // El test anterior mira que el campo EXISTA en el mensaje; este mira que ademas se
            // MAPEE en GestorProductos. Declararlo en los dos sitios y olvidar la asignacion es
            // justo el fallo silencioso que se busca evitar.
            ProductoDTO dto = new ProductoDTO();
            List<string> escalaresRellenados = RellenarEscalares(dto);

            var publisher = A.Fake<ISincronizacionEventPublisher>();
            ProductoSyncMessage capturado = null;
            A.CallTo(() => publisher.PublishEventAsync("sincronizacion-tablas", A<object>.Ignored))
                .Invokes((string _, object message) => capturado = message as ProductoSyncMessage);

            await new GestorProductos(new SincronizacionEventWrapper(publisher)).PublicarProductoSincronizar(dto);

            Assert.IsNotNull(capturado);

            var sinMapear = new List<string>();
            foreach (string nombre in escalaresRellenados)
            {
                PropertyInfo propiedadMensaje = typeof(ProductoSyncMessage).GetProperty(nombre);
                if (propiedadMensaje == null)
                {
                    continue;   // ya lo cubre el primer test
                }
                object valor = propiedadMensaje.GetValue(capturado);
                if (valor == null || EsValorPorDefecto(valor))
                {
                    sinMapear.Add(nombre);
                }
            }

            Assert.AreEqual(0, sinMapear.Count,
                "Estos campos existen en el DTO y en el mensaje, pero GestorProductos no los copia: " +
                string.Join(", ", sinMapear));
        }

        /// <summary>Pone un valor distinto del de por defecto en cada escalar y devuelve sus nombres.</summary>
        private static List<string> RellenarEscalares(ProductoDTO dto)
        {
            var rellenados = new List<string>();
            foreach (PropertyInfo propiedad in typeof(ProductoDTO).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!propiedad.CanWrite || propiedad.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                Type tipo = Nullable.GetUnderlyingType(propiedad.PropertyType) ?? propiedad.PropertyType;
                object valor = null;
                if (tipo == typeof(string))
                {
                    valor = "valor de prueba";
                }
                else if (tipo == typeof(decimal))
                {
                    valor = 12.34M;
                }
                else if (tipo == typeof(bool))
                {
                    valor = true;
                }
                else if (tipo == typeof(short))
                {
                    valor = (short)7;
                }
                else if (tipo == typeof(int))
                {
                    valor = 7;
                }

                if (valor != null)
                {
                    propiedad.SetValue(dto, valor);
                    rellenados.Add(propiedad.Name);
                }
            }
            return rellenados;
        }

        private static bool EsValorPorDefecto(object valor)
        {
            switch (valor)
            {
                case string texto:
                    return string.IsNullOrEmpty(texto);
                case decimal numero:
                    return numero == 0M;
                case bool booleano:
                    return !booleano;
                case short enteroCorto:
                    return enteroCorto == 0;
                case int entero:
                    return entero == 0;
                default:
                    return false;
            }
        }
    }
}
