using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Comprueba que las tres capas del EDMX (CSDL, SSDL y MSL) casan entre sí y con las clases
    /// generadas. Hace falta porque el EDMX se edita a veces a mano y el refresh de Visual Studio
    /// ha dejado propiedades fantasma más de una vez (#413): un mapeo roto NO da error de
    /// compilación, revienta en runtime la primera vez que alguien usa esa entidad.
    ///
    /// Se lee el XML de los recursos incrustados en vez de levantar EF porque el proyecto de tests
    /// no tiene proveedor de SQL Server registrado (ni falta que hace para esta comprobación).
    /// </summary>
    [TestClass]
    public class MapeoEdmxTests
    {
        private static XNamespace CsdlNs => "http://schemas.microsoft.com/ado/2009/11/edm";
        private static XNamespace SsdlNs => "http://schemas.microsoft.com/ado/2009/11/edm/ssdl";
        private static XNamespace MslNs => "http://schemas.microsoft.com/ado/2009/11/mapping/cs";

        private static XDocument LeerRecurso(string extension)
        {
            Assembly ensamblado = typeof(NVEntities).Assembly;
            string nombre = ensamblado.GetManifestResourceNames()
                .Single(r => r.EndsWith($"NestoEntities.{extension}", StringComparison.OrdinalIgnoreCase));

            using (Stream flujo = ensamblado.GetManifestResourceStream(nombre))
            {
                return XDocument.Load(flujo);
            }
        }

        private static Dictionary<string, XElement> EntidadesPorNombre(XDocument documento, XNamespace ns)
        {
            return documento.Descendants(ns + "EntityType")
                .ToDictionary(e => e.Attribute("Name").Value, e => e);
        }

        [TestMethod]
        public void Edmx_CadaPropiedadDelModeloEstaMapeadaAUnaColumnaQueExiste()
        {
            Dictionary<string, XElement> conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs);
            Dictionary<string, XElement> almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs);
            XDocument msl = LeerRecurso("msl");

            List<string> fallos = new List<string>();

            foreach (XElement mapeoConjunto in msl.Descendants(MslNs + "EntitySetMapping"))
            {
                foreach (XElement mapeoTipo in mapeoConjunto.Elements(MslNs + "EntityTypeMapping"))
                {
                    // TypeName viene como "NVModel.NotificacionBuzon" (o IsTypeOf(...) en jerarquías)
                    string nombreEntidad = mapeoTipo.Attribute("TypeName").Value
                        .Replace("IsTypeOf(", "").Replace(")", "");
                    nombreEntidad = nombreEntidad.Substring(nombreEntidad.LastIndexOf('.') + 1);

                    if (!conceptual.TryGetValue(nombreEntidad, out XElement entidadConceptual))
                    {
                        fallos.Add($"El MSL mapea '{nombreEntidad}', que no existe en el CSDL");
                        continue;
                    }

                    foreach (XElement fragmento in mapeoTipo.Elements(MslNs + "MappingFragment"))
                    {
                        string tablaAlmacen = fragmento.Attribute("StoreEntitySet").Value;

                        if (!almacen.TryGetValue(tablaAlmacen, out XElement entidadAlmacen))
                        {
                            fallos.Add($"'{nombreEntidad}' se mapea a la tabla '{tablaAlmacen}', que no existe en el SSDL");
                            continue;
                        }

                        HashSet<string> columnas = new HashSet<string>(
                            entidadAlmacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value));
                        HashSet<string> propiedades = new HashSet<string>(
                            entidadConceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value));

                        foreach (XElement escalar in fragmento.Elements(MslNs + "ScalarProperty"))
                        {
                            string propiedad = escalar.Attribute("Name").Value;
                            string columna = escalar.Attribute("ColumnName").Value;

                            if (!propiedades.Contains(propiedad))
                            {
                                fallos.Add($"{nombreEntidad}.{propiedad} está en el MSL pero no en el CSDL");
                            }

                            if (!columnas.Contains(columna))
                            {
                                fallos.Add($"{nombreEntidad}.{propiedad} apunta a la columna '{columna}', que no existe en '{tablaAlmacen}'");
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(0, fallos.Count, string.Join(Environment.NewLine, fallos));
        }

        [TestMethod]
        public void Edmx_NingunaEntidadTienePropiedadesFantasma()
        {
            // Una propiedad en el CSDL que no exista en la clase generada es justo lo que dejó el
            // refresh del EDMX en #413, y EF revienta al materializar.
            Assembly ensamblado = typeof(NVEntities).Assembly;
            Dictionary<string, XElement> conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs);

            List<string> fantasmas = new List<string>();

            foreach (KeyValuePair<string, XElement> entidad in conceptual)
            {
                Type tipo = ensamblado.GetType($"NestoAPI.Models.{entidad.Key}");

                if (tipo == null)
                {
                    continue; // sin clase POCO con ese nombre: no es cosa de este test
                }

                foreach (XElement propiedad in entidad.Value.Elements(CsdlNs + "Property"))
                {
                    string nombre = propiedad.Attribute("Name").Value;

                    if (tipo.GetProperty(nombre) == null)
                    {
                        fantasmas.Add($"{entidad.Key}.{nombre} está en el EDMX pero no en la clase");
                    }
                }
            }

            Assert.AreEqual(0, fantasmas.Count, string.Join(Environment.NewLine, fantasmas));
        }

        [TestMethod]
        public void Edmx_NotificacionesBuzon_EstaCompletaEnLasTresCapas()
        {
            string[] esperadas =
            {
                "Id", "Usuario", "Empresa", "Vendedor", "Cliente", "Contacto", "Aplicacion",
                "Titulo", "Cuerpo", "Datos", "FechaCreacion", "FechaLeida", "FechaEliminada"
            };

            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["NotificacionBuzon"];
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["NotificacionesBuzon"];
            XElement mapeo = LeerRecurso("msl").Descendants(MslNs + "EntitySetMapping")
                .Single(m => m.Attribute("Name").Value == "NotificacionesBuzon");

            CollectionAssert.AreEquivalent(esperadas,
                conceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                almacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                mapeo.Descendants(MslNs + "ScalarProperty").Select(p => p.Attribute("Name").Value).ToArray());
        }
    }
}
