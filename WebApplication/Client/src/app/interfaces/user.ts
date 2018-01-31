import { Balance } from './balance';

export interface User {
  id          ?: string;
  name        : string;
  email       ?: string;
  tfaEnabled  ?: boolean;
  verifiedL0  ?: boolean;
  verifiedL1  ?: boolean;
  challenges ?: string[];
  ethAddress ?: string;
  balance    ?: Balance;
  social     ?: {
    facebook  : string|null,
    github    : string|null,
    vkontakte : string|null,
    google    : string|null
  };
}
