using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaFamiliasEspecialesEstado9 : EtiquetaFamiliasEspeciales
    {
        
        public EtiquetaFamiliasEspecialesEstado9(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
            
        }

        public new string Nombre=>"Familias Especiales Estado 9";

        
        private void CrearConsulta(string vendedor, DateTime fecha)
        {
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>
                    FamiliasIncluidas.Contains(l.Familia.ToLower()) &&
                    !l.Grupo.ToLower().Equals("otros aparatos", StringComparison.OrdinalIgnoreCase) &&
                    listaVendedores.Contains(l.Vendedor)
                );
        }

        public new static string[] FamiliasIncluidas = { "eva visnu", "santhilea", "max2origin", "mina", "apraise", "maderas", "diagmyskin", "cursos" };
        
    }
}