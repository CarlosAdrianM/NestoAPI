using NestoAPI.Models.Novedades;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Novedades
{
    public interface IServicioNovedades
    {
        List<NovedadDTO> LeerNovedadesPublicadas();
    }
}
