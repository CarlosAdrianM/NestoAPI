using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public abstract class ComisionesAnualesBase
    {
        // Creamos esta clase abstracta para poder quitar métodos del servicio y hacerles test. Ej: LeerResumenAnno
        protected readonly IServicioComisionesAnuales _servicio;
        private const string GENERAL = "General";

        protected ComisionesAnualesBase(IServicioComisionesAnuales servicio)
        {
            _servicio = servicio;
            Etiquetas = NuevasEtiquetas;
        }
        
        public virtual ICollection<IEtiquetaComision> Etiquetas { get; private set; }
        public virtual ICollection<IEtiquetaComision> NuevasEtiquetas { get; private set; } // debería ser init cuando actualicemos a C# 9

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            var etiquetas = NuevasEtiquetas;

            var listaVendedores = _servicio.ListaVendedores(vendedor);
            List<ComisionAnualResumenMes> resumenDb = _servicio.LeerComisionesAnualesResumenMes(listaVendedores, anno);

            if (resumenDb == null || !resumenDb.Any())
            {
                return new Collection<ResumenComisionesMes>();
            }

            byte mesAnterior = resumenDb.First().Mes;

            ICollection<ResumenComisionesMes> resumenAnno = new Collection<ResumenComisionesMes>();
            ResumenComisionesMes resumenMes;
            try
            {
                resumenMes = new ResumenComisionesMes
                {
                    Vendedor = vendedor,
                    Anno = anno,
                    Mes = mesAnterior,
                    Etiquetas = etiquetas.Select(etiqueta => (IEtiquetaComision)etiqueta.Clone()).ToList()
                };
            }
            catch (Exception ex)
            {
                throw ex;
            }
            foreach (ComisionAnualResumenMes resumenMesDB in resumenDb)
            {
                if (mesAnterior != resumenMesDB.Mes)
                {
                    resumenAnno.Add(resumenMes);
                    resumenMes = new ResumenComisionesMes
                    {
                        Vendedor = resumenMesDB.Vendedor,
                        Anno = resumenMesDB.Anno,
                        Mes = resumenMesDB.Mes,
                        Etiquetas = etiquetas.Select(etiqueta => (IEtiquetaComision)etiqueta.Clone()).ToList()
                    };
                    mesAnterior = resumenMesDB.Mes;
                }

                try
                {
                    // si pasamos resumenMesDB por parámetro a la etiqueta y hacemos las asignaciones desde ahí, nos evitamos usar GENERAL
                    IEtiquetaComision etiquetaComision = resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single();
                    if (etiquetaComision is IEtiquetaComisionVenta)
                    {
                        (etiquetaComision as IEtiquetaComisionVenta).Venta += resumenMesDB.Venta;
                    }
                    else
                    {
                        throw new Exception("Tipo de etiqueta no contemplado");
                    }
                    resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Tipo += resumenMesDB.Tipo;
                    if (resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().EsComisionAcumulada) // General es la única que es acumulada
                    {
                        resumenMes.Etiquetas.Where(e => e.Nombre == resumenMesDB.Etiqueta).Single().Comision = resumenMesDB.Comision;
                    }
                }
                catch
                {
                    Console.WriteLine($"Etiqueta {resumenMesDB.Etiqueta} no es válida en la tabla de resúmenes de comisiones del vendedor {resumenMesDB.Vendedor}");
                }

            }
            resumenAnno.Add(resumenMes);

            return resumenAnno;
        }
    }
}