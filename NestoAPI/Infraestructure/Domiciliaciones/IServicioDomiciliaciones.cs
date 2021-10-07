using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Domiciliaciones
{
    public interface IServicioDomiciliaciones
    {
        ICollection<EfectoDomiciliado> LeerDomiciliacionesDia(DateTime dia);
    }
}