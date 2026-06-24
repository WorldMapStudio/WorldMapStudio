import {Redirect} from '@docusaurus/router';
import useBaseUrl from '@docusaurus/useBaseUrl';
import type {ReactNode} from 'react';

export default function Home(): ReactNode {
  return <Redirect to={useBaseUrl('/docs/intro')} />;
}
